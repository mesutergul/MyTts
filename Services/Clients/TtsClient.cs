using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using ElevenLabs;
using ElevenLabs.Models;
using ElevenLabs.TextToSpeech;
using ElevenLabs.Voices;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Models;
using MyTts.Storage;
using MyTts.Helpers;
using MyTts.Storage.Interfaces;
using MyTts.Services.Interfaces;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using System.Threading;

namespace MyTts.Services.Clients
{
    public class TtsClient : ITtsClient, IAsyncDisposable
    {
        private readonly ElevenLabsClient _elevenLabsClient;
        private readonly StorageClient? _googleStorageClient;
        private readonly ICloudTtsClient _geminiTtsClient;
        private readonly IRedisCacheService? _cache;
        private readonly IOptions<ElevenLabsConfig> _config;
        private readonly ILogger<TtsClient> _logger;
        private readonly IMp3StreamMerger _mp3StreamMerger;
        private readonly ILocalStorageClient _localStorageClient;
        private readonly SemaphoreSlim _semaphore;
        private readonly string? _bucketName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly StorageConfiguration _storageConfig;
        private readonly ConcurrentDictionary<string, Voice> _voiceCache;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
        private const int MaxConcurrentOperations = 10;
        private const int BufferSize = 128 * 1024; // 128KB buffer size
        private static readonly ThreadLocal<Random> _random = new(() => new Random());
        private bool _disposed;
        private readonly INotificationService _notificationService;

        public TtsClient(
            ElevenLabsClient elevenLabsClient,
            ICloudTtsClient geminiTtsClient,
            IOptions<ElevenLabsConfig> config,
            IOptions<StorageConfiguration> storageConfig,
            ILocalStorageClient storage,
            IRedisCacheService? cache,
            IMp3StreamMerger mp3StreamMerger,
            INotificationService notificationService,
            ILogger<TtsClient> logger)
        {
            _elevenLabsClient = elevenLabsClient ?? throw new ArgumentNullException(nameof(elevenLabsClient));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _storageConfig = storageConfig?.Value ?? throw new ArgumentNullException(nameof(storageConfig));
            _localStorageClient = storage ?? throw new ArgumentNullException(nameof(storage));
            _cache = cache;
            _geminiTtsClient = geminiTtsClient ?? throw new ArgumentNullException(nameof(geminiTtsClient));
            _mp3StreamMerger = mp3StreamMerger ?? throw new ArgumentNullException(nameof(mp3StreamMerger));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _semaphore = new SemaphoreSlim(MaxConcurrentOperations);
            _voiceCache = new ConcurrentDictionary<string, Voice>();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Configure retry policy
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(exception, 
                            "Retry {RetryCount} after {Delay}ms for operation {OperationKey}", 
                            retryCount, timeSpan.TotalMilliseconds, context.OperationKey);
                    });

            // Configure circuit breaker policy
            _circuitBreakerPolicy = Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 2,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: async (exception, duration) =>
                    {
                        _logger.LogWarning(exception,
                            "Circuit breaker opened for {Duration} seconds due to {ExceptionType}",
                            duration.TotalSeconds, exception.GetType().Name);
                        await _notificationService.SendNotificationAsync(
                            "Circuit Breaker Opened",
                            $"Service is temporarily unavailable for {duration.TotalSeconds} seconds due to {exception.GetType().Name}",
                            NotificationType.Warning);
                    },
                    onReset: async () =>
                    {
                        _logger.LogInformation("Circuit breaker reset - service is healthy again");
                        await _notificationService.SendNotificationAsync(
                            "Circuit Breaker Reset",
                            "Service is healthy and accepting requests again",
                            NotificationType.Success);
                    },
                    onHalfOpen: async () =>
                    {
                        _logger.LogInformation("Circuit breaker half-open - testing service health");
                        await _notificationService.SendNotificationAsync(
                            "Circuit Breaker Testing",
                            "Testing service health before fully reopening",
                            NotificationType.Info);
                    });

            // Initialize Google Cloud Storage if configured
            if (_storageConfig.Disks.TryGetValue("gcloud", out var gcloudDisk) && gcloudDisk.Enabled && gcloudDisk.Config != null)
            {
                try
                {
                    _bucketName = gcloudDisk.Config.BucketName;
                    if (!string.IsNullOrEmpty(gcloudDisk.Config.AuthJson))
                    {
                        _googleStorageClient = StorageClient.Create(Google.Apis.Auth.OAuth2.GoogleCredential
                            .FromJson(gcloudDisk.Config.AuthJson)
                            .CreateScoped(Google.Apis.Storage.v1.StorageService.ScopeConstants.DevstorageFullControl));
                        _logger.LogInformation("Google Cloud Storage client initialized successfully");
                    }
                    else
                    {
                        _logger.LogWarning("Google Cloud Storage auth JSON is not configured");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Google Cloud Storage client");
                }
            }
            else
            {
                _logger.LogInformation("Google Cloud Storage is not configured or disabled");
            }
        }

        private async Task<T> ExecuteWithPoliciesAsync<T>(Func<Task<T>> action, int id, CancellationToken cancellationToken)
        {
            try
            {
                // Apply both policies to the action
            return await _circuitBreakerPolicy
                .WrapAsync(_retryPolicy)
                    .ExecuteAsync(async () => 
                    {
                        var content = await action();
                        if (content is string text)
                        {
                            if (ContainsBlockedContent(text, id))
                            {
                                _logger.LogWarning("Content blocked due to policy violation for ID {Id}", id);
                                throw new InvalidOperationException($"Content violates service policy for ID {id}");
                            }
                        }
                        return content;
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing TTS request for ID {Id}", id);
                throw;
            }
        }

        private bool ContainsBlockedContent(string text, int id)
        {
            // Define blocked content categories
            var blockedCategories = new Dictionary<string, string[]>
            {
                ["violence"] = new[] {
                    // English
                    "kill", "murder", "attack", "weapon", "gun", "bomb", "terrorist",
                    "suicide", "abuse", "torture", "blood", "gore",
                    // Turkish
                    "öldür", "katliam", "saldırı", "silah", "bomba", "terörist",
                    "intihar", "istismar", "işkence", "kan", "şiddet"
                },
                ["hate"] = new[] {
                    // English
                    "racist", "nazi", "supremacist", "bigot", "hate speech",
                    "discriminate", "prejudice", "intolerant",
                    // Turkish
                    "ırkçı", "nazi", "üstün", "bağnaz", "nefret söylemi",
                    "ayrımcı", "önyargı", "hoşgörüsüz"
                },
                ["explicit"] = new[] {
                    // English
                    "porn", "sex", "nude", "explicit", "adult content",
                    "obscene", "lewd", "vulgar",
                    // Turkish
                    "porno", "seks", "çıplak", "müstehcen", "yetişkin içerik",
                    "edepsiz", "ahlaksız", "kaba"
                },
                ["illegal"] = new[] {
                    // English
                    "drug", "cocaine", "heroin", "meth", "illegal",
                    "hack", "crack", "pirate", "steal",
                    // Turkish
                    "uyuşturucu", "eroin", "metamfetamin", "yasadışı",
                    "hack", "korsan", "çal", "hırsızlık"
                },
                ["harmful"] = new[] {
                    // English
                    "suicide", "self-harm", "abuse", "exploit",
                    "scam", "fraud", "phishing",
                    // Turkish
                    "intihar", "kendine zarar", "istismar", "sömürü",
                    "dolandırıcılık", "sahte", "dolandırma"
                }
            };

            // Check for blocked content in parallel
            var blockedCategory = blockedCategories.AsParallel()
                .FirstOrDefault(category => category.Value.Any(term => 
                    text.Contains(term, StringComparison.OrdinalIgnoreCase)));

            if (blockedCategory.Key != null)
            {
                _logger.LogWarning("Content blocked for ID {Id} due to {Category} policy violation", id, blockedCategory.Key);
                // Fire and forget notification
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notificationService.SendNotificationAsync(
                            "Content Policy Violation",
                            $"Content blocked for ID {id} due to {blockedCategory.Key} policy violation",
                            NotificationType.Warning);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send notification for content policy violation for ID {Id}", id);
                    }
                });
                return true;
            }

            // Check for excessive punctuation or special characters using Span<char> for better performance
            var specialCharCount = 0;
            foreach (var c in text.AsSpan())
            {
                if (!char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c))
                {
                    specialCharCount++;
                }
            }

            if (specialCharCount > text.Length * 0.3)
            {
                _logger.LogWarning("Content blocked for ID {Id} due to excessive special characters", id);
                return true;
            }

            // Check for repeated characters using a more efficient approach
            var charCounts = new Dictionary<char, int>();
            foreach (var c in text.AsSpan())
            {
                if (charCounts.TryGetValue(c, out var count))
                {
                    if (count >= 10)
                    {
                        _logger.LogWarning("Content blocked for ID {Id} due to character repetition", id);
                        return true;
                    }
                    charCounts[c] = count + 1;
                }
                else
                {
                    charCounts[c] = 1;
                }
            }

            return false;
        }

        public async Task<string> ProcessContentsAsync(
                 IEnumerable<HaberSummaryDto> allNews,
                 IEnumerable<HaberSummaryDto> neededNews,
                 IEnumerable<HaberSummaryDto> savedNews,
                 string language,
                 AudioType fileType,
                 CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(neededNews);
            var allNewsList = allNews.ToList();
            var contentsNeededList = neededNews.ToList();
            var contentsSavedList = savedNews.ToList();

            _logger.LogInformation("Processing {Count} needed and {SavedCount} saved contents",
                contentsNeededList.Count, contentsSavedList.Count);

            if (!contentsNeededList.Any() && !contentsSavedList.Any())
            {
                return string.Empty;
            }

            try
            {
                //var simdi = DateTime.Now;
                //var anchorText = TurkishDateTimeText.GunlukSeslendirmeMetniOlustur(simdi);
                //var anchor = new HaberSummaryDto()
                //{
                //    Baslik = "Seslendirme",
                //    IlgiId = TurkishDateTimeText.GetCompactTimeId(simdi),
                //    Ozet = anchorText
                //};


                // Process needed and saved news in parallel
                var processingTasks = new List<Task<(int id, AudioProcessor Processor)>>(
                    contentsNeededList.Count + contentsSavedList.Count);
                //{
                //    ProcessContentAsync(
                //    anchor.Ozet,
                //    anchor.IlgiId,
                //    language,
                //    fileType,
                //    cancellationToken)
                //};
                // Add tasks for needed news
                processingTasks.AddRange(contentsNeededList.Select(content =>
                    ProcessContentAsync(
                        content.Ozet,
                        content.IlgiId,
                        language,
                        fileType,
                        cancellationToken)));

                // Add tasks for saved news
                processingTasks.AddRange(contentsSavedList.Select(content =>
                    ProcessSavedContentAsync(content.IlgiId, fileType, cancellationToken)));

                // Wait for all tasks to complete
                var results = await Task.WhenAll(processingTasks);
                // Create processors dictionary for efficient lookup
                var processedFiles = results.ToDictionary(r => r.id, r => r.Processor);
               // var AnchorProcessor = processedFiles[anchor.IlgiId];
                // Build final list in original order
                var processors = allNewsList
                    .Where(news => processedFiles.ContainsKey(news.IlgiId))
                    .Select(news => processedFiles[news.IlgiId])
                    .ToList();
              //  processors.Insert(0, AnchorProcessor);

                if (processors.Count > 0)
                {
                    _logger.LogInformation("Successfully processed {Count} files", processors.Count);

                    // For a single file, return it directly
                    if (processors.Count == 1)
                    {
                        return $"single_{Guid.NewGuid()}.mp3";
                    }

                    // For multiple files, merge them in background
                    string breakAudioPath = StoragePathHelper.GetFullPath("separator", fileType);
                    string startAudioPath = StoragePathHelper.GetFullPath("merged_haber_basi", fileType);
                    string endAudioPath = StoragePathHelper.GetFullPath("merged_haber_sonu", fileType);

                    breakAudioPath= await checkFilePaths(breakAudioPath, cancellationToken);
                    startAudioPath = await checkFilePaths(startAudioPath, cancellationToken);
                    endAudioPath = await checkFilePaths(endAudioPath, cancellationToken);
                    // Start merge operation in background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                            var retryCount = 0;
                            var merged = await _retryPolicy.ExecuteAsync(async () =>
                            {
                                try
                                {
                                    return await _mp3StreamMerger.MergeMp3ByteArraysAsync(
                                        processors,
                                        _storageConfig.BasePath,
                                        fileType,
                                        breakAudioPath,
                                        startAudioPath,
                                        endAudioPath,
                                        cancellationToken);
                                }
                                catch (Exception ex)
                                {
                                    retryCount++;
                                    _logger.LogWarning(ex, "Failed to merge MP3 files, attempt {RetryCount}", retryCount);
                                    throw;
                                }
                            });
                            stopwatch.Stop();

                            _logger.LogInformation(
                                "Merged {Count} MP3 files in {ElapsedMilliseconds}ms",
                                processors.Count,
                                stopwatch.ElapsedMilliseconds);

                            // Send notification about successful merge
                            await _notificationService.SendNotificationAsync(
                                "MP3 Merge Completed",
                                $"Successfully merged {processors.Count} files in {stopwatch.ElapsedMilliseconds}ms",
                                NotificationType.Success);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to merge MP3 files after all retries");
                            await _notificationService.SendErrorNotificationAsync(
                                "MP3 Merge Failed",
                                $"Failed to merge {processors.Count} files after all retries",
                                ex);
                        }
                    }, cancellationToken);

                    // Return a temporary ID that can be used to track the merge progress
                    var mergeId = $"merge_{Guid.NewGuid()}";
                    _logger.LogInformation("Started background merge operation with ID: {MergeId}", mergeId);
                    return mergeId;
                }

                _logger.LogWarning("No files were successfully processed");
                return string.Empty;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Content processing was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch processing");
                throw;
            }
        }
        private async Task<string> checkFilePaths(string path, CancellationToken cancellationToken)
        {
            var existsResult = await _localStorageClient.FileExistsAsync(path, cancellationToken);
            var result = path;
            if (!existsResult.IsSuccess || !existsResult.Data)
            {
                _logger.LogWarning("Break audio file not found at {Path}, merging without breaks", path);
                result = string.Empty;
            }
            return result;
        }
        private async Task<(int id, AudioProcessor FileData)> ProcessGeminiContentAsync(
            string text, int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            var localPath = StoragePathHelper.GetFullPathById(id, fileType);

            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                try
                {
                    _logger.LogInformation("Processing news ID {NewsId} with Gemini: {Title}", id, text);
                    // Assuming 'language' parameter is compatible with Gemini (e.g., "en-US")
                    // VoiceName can be null to use default from config, or specify one if API supports
                    var audioBytes = await _geminiTtsClient.SynthesizeSpeechAsync(
                        text,
                        "tr-TR", // Assuming Turkish for this example, adjust as needed
                        "tr-TR-Standard-A", // Or a specific voice/model name if available and configurable
                        cancellationToken);
                    AudioProcessor audioProcessor= new AudioProcessor(new VoiceClip(audioBytes));
                    return (id, audioProcessor);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content {Id}", id);
                throw;
            }
        }
        public async Task<(int id, AudioProcessor FileData)> ProcessContentAsync(
            string text, int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            var localPath = StoragePathHelper.GetFullPathById(id, fileType);
             
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                try
                {
                    // Get the voice configuration for the specified language
                    if (_config.Value.Feed == null || !_config.Value.Feed.TryGetValue(language, out var languageConfig))
                    {
                        throw new InvalidOperationException($"No voice configuration found for language: {language}");
                    }

                    if (languageConfig.Voices == null || !languageConfig.Voices.Any())
                    {
                        throw new InvalidOperationException($"No voices configured for language: {language}");
                    }

                    // Get a random voice for the language
                    var voices = languageConfig.Voices.ToList();
                    var randomVoice = voices[_random.Value!.Next(voices.Count)];
                    var voiceId = randomVoice.Value;
                    // "Gulsu": "jbJMQWv1eS4YjQ6PCcn6",
                    _logger.LogInformation("Selected voice {VoiceName} ({VoiceId}) for language {Language}", 
                        randomVoice.Key, voiceId, language);
                    
                    // Try to get voice from cache first
                    var voice = await GetOrFetchVoiceAsync(voiceId, id, cancellationToken);
                    var request = await CreateTtsRequestAsync(voice, text);

                    // Generate audio clip from ElevenLabs with retry and circuit breaker policies
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var voiceClip = await ExecuteWithPoliciesAsync(
                        () => _elevenLabsClient.TextToSpeechEndpoint.TextToSpeechAsync(request, null, cancellationToken),
                        id, cancellationToken);
                    stopwatch.Stop();

                    _logger.LogInformation(
                        "Generated audio clip in {ElapsedMilliseconds}ms for text length {TextLength}",
                        stopwatch.ElapsedMilliseconds,
                        text.Length);

                    var audioProcessor = new AudioProcessor(voiceClip);

                    // Launch all I/O-bound tasks in parallel
                    await Task.WhenAll(
                        SaveLocallyAsync(audioProcessor, localPath, cancellationToken),
                        UploadToCloudAsync(audioProcessor, StoragePathHelper.GetStorageKey(id), cancellationToken),
                        StoreMetadataRedisAsync(id, text, localPath, StoragePathHelper.GetStorageKey(id), cancellationToken)
                    );

                    _logger.LogInformation("Processed content {Id}: {LocalPath}", id, localPath);

                    await _notificationService.SendNotificationAsync(
                        "Content Processed Successfully",
                        $"Successfully processed content {id} for language {language}",
                        NotificationType.Success);

                    return (id, audioProcessor);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content {Id}", id);
                await _notificationService.SendErrorNotificationAsync(
                    "Content Processing Failed",
                    $"Failed to process content {id} for language {language}",
                    ex);
                throw;
            }
        }

        private async Task<Voice> GetOrFetchVoiceAsync(string voiceId, int id, CancellationToken cancellationToken)
        {
            // Try to get from cache first
            if (_voiceCache.TryGetValue(voiceId, out var cachedVoice))
            {
                return cachedVoice;
            }

            // If not in cache, fetch and cache it with retry and circuit breaker policies
            var voice = await ExecuteWithPoliciesAsync(
                () => _elevenLabsClient.VoicesEndpoint.GetVoiceAsync(voiceId, withSettings: false, cancellationToken),
                id,
                cancellationToken);
            
            _voiceCache.TryAdd(voiceId, voice);
            return voice;
        }

        private Task<TextToSpeechRequest> CreateTtsRequestAsync(Voice voice, string text)
        {
            var voiceSettings = new VoiceSettings
            {
                Stability = _config.Value.Stability,
                SimilarityBoost = _config.Value.Similarity,
                Style = _config.Value.Style,
                SpeakerBoost = _config.Value.Boost
            };

            var request = new TextToSpeechRequest(
                voice,
                text,
                Encoding.UTF8,
                voiceSettings,
                OutputFormat.MP3_44100_128,
                new Model(_config.Value.Model)
            );

            return Task.FromResult(request);
        }

        private async Task SaveLocallyAsync(AudioProcessor processor, string localPath, CancellationToken cancellationToken)
        {
            string? directory = Path.GetDirectoryName(localPath);
            if (directory != null)
            {
                var dirExistsResult = await _localStorageClient.DirectoryExistsAsync(directory, cancellationToken);
                if (!dirExistsResult.IsSuccess || !dirExistsResult.Data)
                {
                    var createDirResult = await _localStorageClient.CreateDirectoryAsync(directory, cancellationToken);
                    if (!createDirResult.IsSuccess)
                    {
                        throw createDirResult.Error!.Exception;
                    }
                }
            }

            using var stream = await processor.GetStreamForCloudUploadAsync(cancellationToken);
            var saveResult = await _localStorageClient.SaveStreamAsync(stream, localPath, cancellationToken);
            if (!saveResult.IsSuccess)
            {
                throw saveResult.Error!.Exception;
            }

            _logger.LogInformation("Saved file locally: {LocalPath}", localPath);
        }

        private async Task UploadToCloudAsync(AudioProcessor processor, string fileName, CancellationToken cancellationToken)
        {
            if (_googleStorageClient == null || string.IsNullOrEmpty(_bucketName))
            {
                _logger.LogWarning("Skipping cloud upload because GCS is not configured.");
                return;
            }
            
            await using var uploadStream = await processor.GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);

            var uploadOptions = new UploadObjectOptions
            {
                ChunkSize = BufferSize,
                PredefinedAcl = PredefinedObjectAcl.PublicRead
            };

            await _googleStorageClient.UploadObjectAsync(
                _bucketName,
                fileName,
                "audio/mpeg",
                uploadStream,
                options: uploadOptions,
                cancellationToken: cancellationToken);
            _logger.LogInformation("Uploaded file to cloud: {FileName}", fileName);
        }

        private async Task StoreMetadataRedisAsync(int id, string text, string localPath, string fileName, CancellationToken cancellationToken)
        {
            if (_cache == null || !await _cache.IsConnectedAsync(cancellationToken)) return;
            var metadata = new AudioMetadata
            {
                Id = id,
                Text = text,
                LocalPath = localPath,
                GcsPath = fileName,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            await _cache.SetAsync($"audio:{id}", json, TimeSpan.FromDays(7), cancellationToken);
            _logger.LogInformation("Stored metadata in Redis for {Id}", id);
        }

        public async Task<(int id, AudioProcessor Processor)> ProcessSavedContentAsync(
            int ilgiId,
            AudioType fileType,
            CancellationToken cancellationToken)
        {
            try
            {
                string fullPath = StoragePathHelper.GetFullPathById(ilgiId, fileType);
                var readResult = await _localStorageClient.ReadAllBytesAsync(fullPath, cancellationToken);
                if (!readResult.IsSuccess)
                {
                    throw readResult.Error!.Exception;
                }
                var voiceClip = new VoiceClip(readResult.Data!);
                var audioProcessor = new AudioProcessor(voiceClip);
                return (ilgiId, audioProcessor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing saved content {Id}", ilgiId);
                throw;
            }
        }
public async Task<AudioProcessor> ConvertStreamToAudioProcessorAsync(
    Stream audioStream, 
    CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(audioStream);
    
    // Use MemoryStream with initial capacity if you know approximate size
    using var memoryStream = audioStream.CanSeek 
        ? new MemoryStream((int)audioStream.Length) 
        : new MemoryStream();
        
    await audioStream.CopyToAsync(memoryStream, 81920, cancellationToken); // Use 80KB buffer
    
    // Create VoiceClip directly from MemoryStream's buffer to avoid extra copy
    var audioData = memoryStream.ToArray();
    var voiceClip = new VoiceClip(audioData);
    return new AudioProcessor(voiceClip);
}
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Dispose();
            if (_cache is IAsyncDisposable disposableCache)
            {
                await disposableCache.DisposeAsync();
            }
        }

        private record AudioMetadata
        {
            public required int Id { get; init; }
            public required string Text { get; init; }
            public required string LocalPath { get; init; }
            public required string GcsPath { get; init; }
            public required DateTime Timestamp { get; init; }
        }
    }
} 