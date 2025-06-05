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
using MyTts.Services.Constants;

namespace MyTts.Services.Clients
{
    public class TtsClient : ITtsClient, IAsyncDisposable
    {
        private readonly ResilientElevenLabsClient _resilientElevenLabsClient;
        private readonly StorageClient? _googleStorageClient;
        private readonly ICloudTtsClient _geminiTtsClient;
        private readonly IRedisCacheService _cache;
        private readonly IOptions<ElevenLabsConfig> _config;
        private readonly ILogger<TtsClient> _logger;
        private readonly IMp3StreamMerger _mp3StreamMerger;
        private readonly ILocalStorageClient _localStorageClient;
        private readonly string? _bucketName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly StorageConfiguration _storageConfig;
        private readonly ConcurrentDictionary<string, Voice> _voiceCache;
        private const int BufferSize = 128 * 1024; // 128KB buffer size
        private static readonly ThreadLocal<Random> _random = new(() => new Random());
        private bool _disposed;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public TtsClient(
            ICloudTtsClient geminiTtsClient,
            ResilientElevenLabsClient resilientElevenLabsClient,
            IOptions<ElevenLabsConfig> config,
            IOptions<StorageConfiguration> storageConfig,
            ILocalStorageClient storage,
            IRedisCacheService cache,
            IMp3StreamMerger mp3StreamMerger,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<TtsClient> logger)
        {
            _resilientElevenLabsClient = resilientElevenLabsClient ?? throw new ArgumentNullException(nameof(resilientElevenLabsClient));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _storageConfig = storageConfig?.Value ?? throw new ArgumentNullException(nameof(storageConfig));
            _localStorageClient = storage ?? throw new ArgumentNullException(nameof(storage));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _geminiTtsClient = geminiTtsClient ?? throw new ArgumentNullException(nameof(geminiTtsClient));
            _mp3StreamMerger = mp3StreamMerger ?? throw new ArgumentNullException(nameof(mp3StreamMerger));
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _voiceCache = new ConcurrentDictionary<string, Voice>();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
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
                // Process needed and saved news in parallel with proper cancellation handling
                var processingTasks = new List<Task<(int id, AudioProcessor Processor)>>(
                    contentsNeededList.Count + contentsSavedList.Count);

                // Add tasks for needed news with cancellation handling
                foreach (var content in contentsNeededList)
                {
                    processingTasks.Add(ProcessContentAsync(
                        content.Ozet,
                        content.IlgiId,
                        language,
                        fileType,
                        cancellationToken));
                }

                // Add tasks for saved news with cancellation handling
                foreach (var content in contentsSavedList)
                {
                    processingTasks.Add(ProcessSavedContentAsync(
                        content.IlgiId,
                        fileType,
                        cancellationToken));
                }

                // Wait for all tasks to complete, handling cancellations
                var results = await Task.WhenAll(processingTasks);

                // Create processors dictionary for efficient lookup
                var processedFiles = results.ToDictionary(r => r.id, r => r.Processor);

                // Build final list in original order
                var processors = allNewsList
                    .Where(news => processedFiles.ContainsKey(news.IlgiId))
                    .Select(news => processedFiles[news.IlgiId])
                    .ToList();

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

                    breakAudioPath = await CheckFilePaths(breakAudioPath, cancellationToken);
                    startAudioPath = await CheckFilePaths(startAudioPath, cancellationToken);
                    endAudioPath = await CheckFilePaths(endAudioPath, cancellationToken);

                    // Start merge operation in background with proper cancellation handling
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                            await ExecuteMergeOperationAsync(
                                processors,
                                breakAudioPath,
                                startAudioPath,
                                endAudioPath,
                                fileType,
                                CancellationToken.None); // Use None for background task
                            stopwatch.Stop();

                            _logger.LogInformation(
                                "Merged {Count} MP3 files in {ElapsedMilliseconds}ms",
                                processors.Count,
                                stopwatch.ElapsedMilliseconds);

                            // Create a new scope for notification
                            using var scope = _serviceScopeFactory.CreateScope();
                            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                            await notificationService.SendNotificationAsync(
                                "MP3 Merge Completed",
                                $"Successfully merged {processors.Count} files in {stopwatch.ElapsedMilliseconds}ms",
                                NotificationType.Success);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to merge MP3 files after all retries");
                            // Create a new scope for error notification
                            using var scope = _serviceScopeFactory.CreateScope();
                            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                            await notificationService.SendErrorNotificationAsync(
                                "MP3 Merge Failed",
                                $"Failed to merge {processors.Count} files after all retries",
                                ex);
                        }
                    }, CancellationToken.None); // Use None for background task

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
                _logger.LogWarning("Content processing was canceled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch processing");
                throw;
            }
        }
        private async Task ExecuteMergeOperationAsync(
            List<AudioProcessor> processors,
            string breakAudioPath,
            string startAudioPath,
            string endAudioPath,
            AudioType fileType,
            CancellationToken cancellationToken)
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                await _mp3StreamMerger.MergeMp3ByteArraysAsync(
                    processors,
                    _storageConfig.BasePath,
                    fileType,
                    breakAudioPath,
                    startAudioPath,
                    endAudioPath,
                    cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Merged {Count} MP3 files in {ElapsedMilliseconds}ms",
                    processors.Count,
                    stopwatch.ElapsedMilliseconds);

                // Create a new scope for notification
                using var scope = _serviceScopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notificationService.SendNotificationAsync(
                    "MP3 Merge Completed",
                    $"Successfully merged {processors.Count} files in {stopwatch.ElapsedMilliseconds}ms",
                    NotificationType.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to merge MP3 files");
                // Create a new scope for error notification
                using var scope = _serviceScopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notificationService.SendErrorNotificationAsync(
                    "MP3 Merge Failed",
                    $"Failed to merge {processors.Count} files",
                    ex);
                throw;
            }
        }
        private async Task<string> CheckFilePaths(string path, CancellationToken cancellationToken)
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
        public async Task<(int id, AudioProcessor FileData)> ProcessContentAsync(
            string text,
            int id,
            string language,
            AudioType fileType,
            CancellationToken cancellationToken)
        {
            var localPath = StoragePathHelper.GetFullPathById(id, fileType);
            string redisKey = RedisKeys.FormatKey(RedisKeys.TTS_INDIVIDUAL_MP3_KEY, id);
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

                _logger.LogInformation("Selected voice {VoiceName} ({VoiceId}) for language {Language}",
                    randomVoice.Key, voiceId, language);

                // Try to get voice from cache first
                var voice = await GetOrFetchVoiceAsync(voiceId, id, cancellationToken);
                var request = await CreateTtsRequestAsync(voice, text);

                // Generate audio clip from ElevenLabs with retry and circuit breaker policies
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var voiceClip = await _resilientElevenLabsClient.TextToSpeechAsync(request, voiceId, cancellationToken);
                stopwatch.Stop();

                _logger.LogInformation(
                    "Generated audio clip in {ElapsedMilliseconds}ms for text length {TextLength}",
                    stopwatch.ElapsedMilliseconds,
                    text.Length);

                byte[] audioData = voiceClip.ClipData.ToArray();
                await _cache.SetAsync(redisKey, audioData, RedisKeys.INDIVIDUAL_MP3_DURATION, cancellationToken);
                var audioProcessor = new AudioProcessor(voiceClip);

                // Launch all I/O-bound tasks in parallel
                await Task.WhenAll(
                    SaveLocallyAsync(audioProcessor, localPath, cancellationToken),
                    UploadToCloudAsync(audioProcessor, StoragePathHelper.GetStorageKey(id), cancellationToken),
                    StoreMetadataRedisAsync(id, text, localPath, StoragePathHelper.GetStorageKey(id), cancellationToken)
                );

                _logger.LogInformation("Processed content {Id}: {LocalPath}", id, localPath);

                // Create a new scope for success notification
                using var scope = _serviceScopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notificationService.SendNotificationAsync(
                    "Content Processed Successfully",
                    $"Successfully processed content {id} for language {language}",
                    NotificationType.Success);

                return (id, audioProcessor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content {Id}", id);
                // Create a new scope for error notification
                using var scope = _serviceScopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notificationService.SendErrorNotificationAsync(
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
            var voice = await _resilientElevenLabsClient.GetVoiceAsync(voiceId, withSettings: false, cancellationToken);

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
            if (!await _cache.IsConnectedAsync(cancellationToken)) return;
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
                AudioProcessor audioProcessor = null!;
                string localPath = StoragePathHelper.GetFullPathById(ilgiId, fileType);
                // --- Try to load from Redis first ---
                if (await _cache.IsConnectedAsync(cancellationToken))
                {
                    try
                    {
                        string redisKey = RedisKeys.FormatKey(RedisKeys.TTS_INDIVIDUAL_MP3_KEY, ilgiId);
                        var cachedData = await _cache.GetAsync<byte[]>(redisKey, cancellationToken);
                        if (cachedData != null)
                        {
                            audioProcessor = new AudioProcessor(new VoiceClip(cachedData));
                            _logger.LogInformation("Loaded audio for {Id} from Redis cache.", ilgiId);
                            if ((await _localStorageClient.FileExistsAsync(localPath, cancellationToken)).IsSuccess)
                            {
                                _logger.LogInformation("Local file already exists for {Id}, skipping local save.", ilgiId);
                            }
                            else
                            {
                                // Save locally in background without waiting
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await SaveLocallyAsync(audioProcessor, localPath, CancellationToken.None);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Failed to save audio locally for {Id}", ilgiId);
                                    }
                                }, CancellationToken.None);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Redis operation was canceled for {Id}", ilgiId);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load from Redis for {Id}, will try local storage", ilgiId);
                    }
                }

                if (audioProcessor == null)
                {
                    // --- If not in cache, load from local storage ---
                    var readResult = await _localStorageClient.ReadAllBytesAsync(localPath, cancellationToken);
                    if (!readResult.IsSuccess)
                    {
                        throw readResult.Error!.Exception;
                    }
                    audioProcessor = new AudioProcessor(new VoiceClip(readResult.Data!));
                }

                return (ilgiId, audioProcessor);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Operation was canceled for content {Id}", ilgiId);
                throw;
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
            await _resilientElevenLabsClient.DisposeAsync();
            await _mp3StreamMerger.DisposeAsync();
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