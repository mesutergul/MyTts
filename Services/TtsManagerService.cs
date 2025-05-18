using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElevenLabs;
using ElevenLabs.Models;
using ElevenLabs.TextToSpeech;
using ElevenLabs.Voices;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Models;
using MyTts.Services.Interfaces;
using MyTts.Storage;
using MyTts.Services.Constants;
using MyTts.Helpers;

namespace MyTts.Services
{
    public class TtsManagerService : ITtsManagerService, IAsyncDisposable
    {
        private readonly ElevenLabsClient _elevenLabsClient;
        private readonly StorageClient? _storageClient;
        private readonly IRedisCacheService? _cache;
        private readonly IOptions<ElevenLabsConfig> _config;
        private readonly ILogger<TtsManagerService> _logger;
        private readonly IMp3StreamMerger _mp3StreamMerger;
        private readonly ILocalStorageService _localStorage;
        private readonly SemaphoreSlim _semaphore;
        private readonly string? _bucketName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly StorageConfiguration _storageConfig;
        private const int MaxConcurrentOperations = 10;
        private readonly Random _random = new Random();

        public TtsManagerService(
            ElevenLabsClient elevenLabsClient,
            IOptions<ElevenLabsConfig> config,
            IOptions<StorageConfiguration> storageConfig,
            ILocalStorageService localStorage,
            IRedisCacheService cache,
            IMp3StreamMerger mp3StreamMerger,
            ILogger<TtsManagerService> logger)
        {
            _elevenLabsClient = elevenLabsClient ?? throw new ArgumentNullException(nameof(elevenLabsClient));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _storageConfig = storageConfig?.Value ?? throw new ArgumentNullException(nameof(storageConfig));
            _localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));
            _cache = cache;
            _mp3StreamMerger = mp3StreamMerger ?? throw new ArgumentNullException(nameof(mp3StreamMerger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _semaphore = new SemaphoreSlim(MaxConcurrentOperations);
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Initialize StoragePathHelper
            StoragePathHelper.Initialize(_storageConfig);
        }

        private async Task<(int id, AudioProcessor Processor)> ProcessSavedContentAsync(
            int ilgiId, 
            AudioType fileType, 
            CancellationToken cancellationToken)
        {
            try
            {
                string fullPath = StoragePathHelper.GetFullPathById(ilgiId, fileType);
                byte[] stream = await _localStorage.ReadAllBytesAsync(fullPath, cancellationToken);
                var voiceClip = new VoiceClip(stream);
                var audioProcessor = new AudioProcessor(voiceClip);
                return (ilgiId, audioProcessor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing saved content {Id}", ilgiId);
                throw;
            }
        }

        private async Task<(int id, AudioProcessor Processor)> ProcessContentWithSemaphoreAsync(
            string content,
            int id,
            string language,
            AudioType fileType,
            CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ProcessContentAsync(content, id, language, fileType, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<(int id, AudioProcessor FileData)> ProcessContentAsync(
            string text, int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            var localPath = StoragePathHelper.GetFullPathById(id, fileType);
             
            try
            {
                // Get the voice configuration for the specified language
                if (!_config.Value.Feed.TryGetValue(language, out var languageConfig))
                {
                    throw new InvalidOperationException($"No voice configuration found for language: {language}");
                }

                // Get a random voice for the language
                var voices = languageConfig.Voices.ToList();
                var randomVoice = voices[_random.Next(voices.Count)];
                var voiceId = randomVoice.Value;
                
                _logger.LogInformation("Selected voice {VoiceName} ({VoiceId}) for language {Language}", randomVoice.Key, voiceId, language);
                
                // Fetch voice and create request in parallel
                var voiceTask = _elevenLabsClient.VoicesEndpoint
                    .GetVoiceAsync(voiceId, withSettings: false, cancellationToken);

                var requestTask = voiceTask.ContinueWith(async vt =>
                {
                    var voice = await vt;
                    return await CreateTtsRequestAsync(voice, text);
                }, cancellationToken, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default).Unwrap();

                // Generate audio clip from ElevenLabs
                VoiceClip voiceClip = await _elevenLabsClient.TextToSpeechEndpoint
                    .TextToSpeechAsync(await requestTask, null, cancellationToken);

                var audioProcessor = new AudioProcessor(voiceClip);

                // Launch all I/O-bound tasks in parallel
                var tasks = new Task[]
                {
                    SaveLocallyAsync(audioProcessor, localPath, cancellationToken),
                    UploadToCloudAsync(audioProcessor, StoragePathHelper.GetStorageKey(id), cancellationToken),
                    StoreMetadataRedisAsync(id, text, localPath, StoragePathHelper.GetStorageKey(id), cancellationToken)
                };

                await Task.WhenAll(tasks);

                _logger.LogInformation("Processed content {Id}: {LocalPath}", id, localPath);
                return (id, audioProcessor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content {Id}", id);
                throw;
            }
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
            if (directory != null && !await _localStorage.DirectoryExistsAsync(directory))
            {
                await _localStorage.CreateDirectoryAsync(directory);
            }

            await _localStorage.SaveStreamToFileAsync(processor, localPath, cancellationToken);
            _logger.LogInformation("Saved file locally: {LocalPath}", localPath);
        }

        private async Task UploadToCloudAsync(AudioProcessor processor, string fileName, CancellationToken cancellationToken)
        {
            if (_storageClient == null || string.IsNullOrEmpty(_bucketName))
            {
                _logger.LogWarning("Skipping cloud upload because GCS is not configured.");
                return;
            }
            
            await using var uploadStream = await processor.GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);

            var uploadOptions = new UploadObjectOptions
            {
                ChunkSize = 262144, // 256KB chunks for better handling
                PredefinedAcl = PredefinedObjectAcl.PublicRead
            };

            await _storageClient.UploadObjectAsync(
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
            if (_cache == null || !await _cache.IsConnectedAsync()) return;
            var metadata = new AudioMetadata
            {
                Id = id,
                Text = text,
                LocalPath = localPath,
                GcsPath = $"gs://{_bucketName}/{fileName}",
                Timestamp = DateTime.UtcNow
            };

            await _cache.SetAsync(string.Format(RedisKeys.TTS_METADATA_KEY, id), metadata, RedisKeys.DEFAULT_METADATA_EXPIRY);
        }

        public async Task<(Stream audioData, string contentType, string fileName)> ProcessContentsAsync(
            IEnumerable<HaberSummaryDto> allNews, 
            IEnumerable<HaberSummaryDto> neededNews, 
            IEnumerable<HaberSummaryDto> savedNews, 
            string language, 
            AudioType fileType, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(neededNews);
            var allNewsList = allNews.ToList();
            var contentsNeededList = neededNews.ToList(); // Materialize once to avoid multiple enumeration
            var contentsSavedList = savedNews.ToList();
            
            _logger.LogInformation("Processing {Count} needed and {SavedCount} saved contents", 
                contentsNeededList.Count, contentsSavedList.Count);
            
            if (!contentsNeededList.Any() && !contentsSavedList.Any())
            {
                return (null, "", "");
            }
                
            try
            {
                var processingNeededTasks = new List<Task<(int id, AudioProcessor Processor)>>(contentsNeededList.Count);
                var processingSavedTasks = new List<Task<(int id, AudioProcessor Processor)>>(contentsSavedList.Count);

                // Process needed news
                foreach (var content in contentsNeededList)
                {
                    processingNeededTasks.Add(ProcessContentWithSemaphoreAsync(
                        content.Baslik + content.Ozet, 
                        content.IlgiId, 
                        language, 
                        fileType, 
                        cancellationToken));
                }

                // Process saved news
                foreach (var content in contentsSavedList)
                {
                    processingSavedTasks.Add(ProcessSavedContentAsync(
                        content.IlgiId, 
                        fileType, 
                        cancellationToken));
                }

                // Wait for all tasks to complete
                var resultsSaved = await Task.WhenAll(processingSavedTasks);
                var resultsNeeded = await Task.WhenAll(processingNeededTasks);

                // Create processors list in the same order as allNews
                var processors = new List<AudioProcessor>(allNewsList.Count);
                var processedFiles = new Dictionary<int, AudioProcessor>();

                // Add needed files to dictionary
                foreach (var result in resultsNeeded)
                {
                    processedFiles[result.id] = result.Processor;
                }

                // Add saved files to dictionary
                foreach (var result in resultsSaved)
                {
                    processedFiles[result.id] = result.Processor;
                }

                // Build final list in original order
                foreach (var news in allNewsList)
                {
                    if (processedFiles.TryGetValue(news.IlgiId, out var processor))
                    {
                        processors.Add(processor);
                    }
                    else
                    {
                        _logger.LogWarning("No processor found for news item with ID {IlgiId}", news.IlgiId);
                    }
                }

                if (processors.Count > 0)
                {
                    _logger.LogInformation("Successfully processed {Count} files", processors.Count);
                    
                    // For a single file, return it directly
                    if (processors.Count == 1)
                    {
                        var stream = await processors[0].GetStreamForCloudUploadAsync(cancellationToken);
                        return (stream, "audio/mpeg", $"single_{Guid.NewGuid()}.mp3");
                    }

                    // For multiple files, merge them
                    string breakAudioPath = StoragePathHelper.GetFullPath("break", fileType);
                    if (!await _localStorage.FileExistsAsync(breakAudioPath))
                    {
                        _logger.LogWarning("Break audio file not found at {Path}, merging without breaks", breakAudioPath);
                        breakAudioPath = null;
                    }

                    var merged = await _mp3StreamMerger.MergeMp3ByteArraysAsync(
                        processors, 
                        _storageConfig.BasePath, 
                        fileType, 
                        breakAudioPath, 
                        cancellationToken);
                    
                    _logger.LogInformation("Merged stream id is {fileName}", merged.fileName);
                    return merged;
                }

                _logger.LogWarning("No files were successfully processed");
                return (null, "", "");
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

        private record AudioMetadata
        {
            public required int Id { get; init; }
            public required string Text { get; init; }
            public required string LocalPath { get; init; }
            public required string GcsPath { get; init; }
            public required DateTime Timestamp { get; init; }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_storageClient != null)
                {
                    await Task.Run(() => _storageClient.Dispose());
                }
                _semaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing TtsManager resources");
            }
        }
    }
}