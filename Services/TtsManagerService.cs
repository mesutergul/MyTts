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
using MyTts.Services.Interfaces;
using MyTts.Storage;
using MyTts.Services.Constants;
using MyTts.Helpers;
using MyTts.Storage.Interfaces;
using MyTts.Storage.Models;

namespace MyTts.Services
{
    public class TtsManagerService : ITtsManagerService, IAsyncDisposable
    {
        private readonly ElevenLabsClient _elevenLabsClient;
        private readonly StorageClient? _googleStorageClient;
        private readonly IRedisCacheService? _cache;
        private readonly IOptions<ElevenLabsConfig> _config;
        private readonly ILogger<TtsManagerService> _logger;
        private readonly IMp3StreamMerger _mp3StreamMerger;
        private readonly ILocalStorageClient _localStorageClient;
        private readonly SemaphoreSlim _semaphore;
        private readonly string? _bucketName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly StorageConfiguration _storageConfig;
        private readonly ConcurrentDictionary<string, Voice> _voiceCache;
        private const int MaxConcurrentOperations = 10;
        private const int BufferSize = 128 * 1024; // 128KB buffer size
        private static readonly ThreadLocal<Random> _random = new(() => new Random());
        private bool _disposed;

        public TtsManagerService(
            ElevenLabsClient elevenLabsClient,
            IOptions<ElevenLabsConfig> config,
            IOptions<StorageConfiguration> storageConfig,
            ILocalStorageClient storage,
            IRedisCacheService cache,
            IMp3StreamMerger mp3StreamMerger,
            ILogger<TtsManagerService> logger)
        {
            _elevenLabsClient = elevenLabsClient ?? throw new ArgumentNullException(nameof(elevenLabsClient));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _storageConfig = storageConfig?.Value ?? throw new ArgumentNullException(nameof(storageConfig));
            _localStorageClient = storage ?? throw new ArgumentNullException(nameof(storage));
            _cache = cache;
            _mp3StreamMerger = mp3StreamMerger ?? throw new ArgumentNullException(nameof(mp3StreamMerger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _semaphore = new SemaphoreSlim(_config.Value.MaxConcurrency ?? MaxConcurrentOperations);
            _voiceCache = new ConcurrentDictionary<string, Voice>();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

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
                var randomVoice = voices[_random.Value!.Next(voices.Count)];
                var voiceId = randomVoice.Value;
                
                _logger.LogInformation("Selected voice {VoiceName} ({VoiceId}) for language {Language}", 
                    randomVoice.Key, voiceId, language);
                
                // Try to get voice from cache first
                var voice = await GetOrFetchVoiceAsync(voiceId, cancellationToken);
                var request = await CreateTtsRequestAsync(voice, text);

                // Generate audio clip from ElevenLabs
                VoiceClip voiceClip = await _elevenLabsClient.TextToSpeechEndpoint
                    .TextToSpeechAsync(request, null, cancellationToken);

                var audioProcessor = new AudioProcessor(voiceClip);

                // Launch all I/O-bound tasks in parallel
                await Task.WhenAll(
                    SaveLocallyAsync(audioProcessor, localPath, cancellationToken),
                    UploadToCloudAsync(audioProcessor, StoragePathHelper.GetStorageKey(id), cancellationToken),
                    StoreMetadataRedisAsync(id, text, localPath, StoragePathHelper.GetStorageKey(id), cancellationToken)
                );

                _logger.LogInformation("Processed content {Id}: {LocalPath}", id, localPath);
                return (id, audioProcessor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content {Id}", id);
                throw;
            }
        }

        private async Task<Voice> GetOrFetchVoiceAsync(string voiceId, CancellationToken cancellationToken)
        {
            // Try to get from cache first
            if (_voiceCache.TryGetValue(voiceId, out var cachedVoice))
            {
                return cachedVoice;
            }

            // If not in cache, fetch and cache it
            var voice = await _elevenLabsClient.VoicesEndpoint
                .GetVoiceAsync(voiceId, withSettings: false, cancellationToken);
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
            var contentsNeededList = neededNews.ToList();
            var contentsSavedList = savedNews.ToList();
            
            _logger.LogInformation("Processing {Count} needed and {SavedCount} saved contents", 
                contentsNeededList.Count, contentsSavedList.Count);
            
            if (!contentsNeededList.Any() && !contentsSavedList.Any())
            {
                return (Stream.Null, "", "");
            }
                
            try
            {
                // Process needed and saved news in parallel
                var processingTasks = new List<Task<(int id, AudioProcessor Processor)>>(
                    contentsNeededList.Count + contentsSavedList.Count);

                // Add tasks for needed news
                processingTasks.AddRange(contentsNeededList.Select(content =>
                    ProcessContentWithSemaphoreAsync(
                        content.Baslik + content.Ozet,
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
                        var stream = await processors[0].GetStreamForCloudUploadAsync(cancellationToken);
                        return (stream, "audio/mpeg", $"single_{Guid.NewGuid()}.mp3");
                    }

                    // For multiple files, merge them
                    string breakAudioPath = StoragePathHelper.GetFullPath("break", fileType);
                    var existsResult = await _localStorageClient.FileExistsAsync(breakAudioPath, cancellationToken);
                    if (!existsResult.IsSuccess || !existsResult.Data)
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
                return (Stream.Null, "", "");
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
            if (!_disposed)
            {
                try
                {
                    if (_googleStorageClient != null)
                    {
                        await Task.Run(() => _googleStorageClient.Dispose());
                    }
                    _semaphore?.Dispose();
                    _voiceCache.Clear();
                    _disposed = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing TtsManager resources");
                }
            }
        }
    }
}