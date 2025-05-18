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
using MyTts.Repositories;
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
        private readonly SemaphoreSlim _semaphore;
        private readonly SemaphoreSlim _semaphoreSql;
        private readonly string? _bucketName;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly StorageConfiguration _storageConfig;
        private readonly IMp3Repository _mp3Repository;
        private const int MaxConcurrentOperations = 10;
        private readonly Random _random = new Random();

        public TtsManagerService(
            ElevenLabsClient elevenLabsClient,
            IOptions<ElevenLabsConfig> config,
            IOptions<StorageConfiguration> storageConfig,
            IMp3Repository mp3Repository,
            IRedisCacheService cache,
            IMp3StreamMerger mp3StreamMerger,
            ILogger<TtsManagerService> logger)
        {
            _elevenLabsClient = elevenLabsClient ?? throw new ArgumentNullException(nameof(elevenLabsClient));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _storageConfig = storageConfig?.Value ?? throw new ArgumentNullException(nameof(storageConfig));
            _mp3Repository = mp3Repository ?? throw new ArgumentNullException(nameof(mp3Repository));
            _cache = cache;
            _mp3StreamMerger = mp3StreamMerger ?? throw new ArgumentNullException(nameof(mp3StreamMerger));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _semaphore = new SemaphoreSlim(MaxConcurrentOperations);
            _semaphoreSql = new SemaphoreSlim(1);
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // Initialize StoragePathHelper
            StoragePathHelper.Initialize(_storageConfig);
        }
        private (StorageClient? client, string? bucketName)? InitializeGoogleCloudStorage(StorageConfiguration config)
        {
            try
            {
                // Get Google Cloud configuration  
                var googleCloudDisk = config.Disks.TryGetValue("gcloud", out var disk) ? disk : null;
                if (googleCloudDisk == null || !googleCloudDisk.Enabled || googleCloudDisk.Config == null || disk?.Config == null)
                {
                    _logger.LogWarning("Google Cloud Storage is not enabled or misconfigured. Skipping cloud upload.");
                    return null;
                }

                // Create StorageClient with builder for more control  
                var clientBuilder = new StorageClientBuilder();

                // If AuthJson is provided in config, use it  
                if (string.IsNullOrEmpty(googleCloudDisk.Config.AuthJson))
                {
                    clientBuilder.CredentialsPath = googleCloudDisk.Config.AuthJson;
                }

                var client = clientBuilder.Build();

                // Verify bucket exists and is accessible  
                try
                {
                    client.GetBucket(googleCloudDisk.Config.BucketName);
                    _logger.LogInformation("Successfully connected to Google Cloud Storage bucket: {BucketName}", googleCloudDisk.Config.BucketName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to access Google Cloud Storage bucket: {BucketName}", googleCloudDisk.Config.BucketName);
                    return null;
                }

                return (client, googleCloudDisk.Config.BucketName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Google Cloud Storage client");
                return null;
            }
        }
        // Optimized version of MergeAudioFilesAsync
        private async Task<(Stream audioData, string contentType, string fileName)> MergeAudioFilesAsync
            (List<AudioProcessor> processors, 
            string basePath, 
            AudioType fileType, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(processors);
            if (processors.Count == 0)
            {
                throw new ArgumentException("No audio processors provided", nameof(processors));
            }

            // For a single file, we can return it directly without merging
            if (processors.Count == 1)
            {
                _logger.LogInformation("Only one processor provided - returning without merge");
                // Memory-efficient way to return a single file
                return await CreateSingleFileResultAsync(processors[0], cancellationToken);
            }

            // Get the break audio file path using StoragePathHelper
            string breakAudioPath = StoragePathHelper.GetFullPath("break", fileType);
            if (!File.Exists(breakAudioPath))
            {
                _logger.LogWarning("Break audio file not found at {Path}, merging without breaks", breakAudioPath);
                breakAudioPath = null;
            }

            // Use the optimized Mp3StreamMerger
            return await _mp3StreamMerger.MergeMp3ByteArraysAsync(processors, basePath, fileType, breakAudioPath, cancellationToken);
        }
        // Helper method for single file scenario
        private async Task<(Stream audioData, string contentType, string fileName)> CreateSingleFileResultAsync(AudioProcessor processor, CancellationToken cancellationToken)
        {
            await using var uploadStream = await processor.GetStreamForCloudUploadAsync(cancellationToken);

            return (uploadStream, "audio/mpeg", $"single_{Guid.NewGuid()}.mp3");
        }
        // Optimized ProcessContentsAsync
        public async Task<(Stream audioData, string contentType, string fileName)> ProcessContentsAsync(
        IEnumerable<HaberSummaryDto> allNews, IEnumerable<HaberSummaryDto> neededNews, IEnumerable<HaberSummaryDto> savedNews, string language, AudioType fileType, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(neededNews);
            var allNewsList = allNews.ToList();
            var contentsNeededList = neededNews.ToList(); // Materialize once to avoid multiple enumeration
            var referenceIds = contentsNeededList.Select(p => p.IlgiId).ToHashSet();
            //var contentsSavedList = allNews
            //    .Where(p => !referenceIds.Contains(p.IlgiId))
            //    .ToList();
            var contentsSavedList=savedNews.ToList();
            _logger.LogInformation("Processing {Count} needed and {SavedCount} saved contents", contentsNeededList.Count, contentsSavedList.Count);
            if (!contentsNeededList.Any() && !contentsSavedList.Any())
            {
                return (null, "", "");
            }
               
            try
            {
                var processingNeededTasks = new List<Task<(int id, AudioProcessor Processor)>>(contentsNeededList.Count);
                var processingSavedTasks = new List<Task<(int id, AudioProcessor Processor)>>(contentsSavedList.Count);
                // Create a dictionary to map IDs to their list indices
                var neededIds = new Dictionary<int, int>();
                var savedIds = new Dictionary<int, int>();

                for (int i = 0; i < contentsNeededList.Count; i++)
                {
                    var content = contentsNeededList[i];
                    neededIds[content.IlgiId] = i;
                    // Create tasks but don't await them yet
                    processingNeededTasks.Add(ProcessContentWithSemaphoreAsync(content.Baslik + content.Ozet, content.IlgiId, language, fileType, cancellationToken));
                }
                for (int i = 0; i < contentsSavedList.Count; i++)
                {
                    var content = contentsSavedList[i];
                    savedIds[content.IlgiId] = i;
                    // Create tasks but don't await them yet
                    processingSavedTasks.Add(ProcessSavedContentAsync(content.IlgiId, fileType, cancellationToken));
                }
                var resultsSaved = await Task.WhenAll(processingSavedTasks);
                // Wait for all tasks to complete
                var resultsNeeded = await Task.WhenAll(processingNeededTasks);

                // Extract the processors
                var processors = new List<AudioProcessor>(allNewsList.Count);
                // Create a lookup map for quick access to results
                var neededDict = new Dictionary<int, AudioProcessor>();
                var savedDict = new Dictionary<int, AudioProcessor>();
                // var processors = results.Select(r => r.Processor).ToList();
                foreach (var result in resultsNeeded)
                {
                    neededDict[result.id] = result.Processor;                  
                }
                foreach (var result in resultsSaved)
                {           
                    savedDict[result.id] = result.Processor;                   
                }
                foreach (var news in allNewsList)
                {
                    int ilgiId = news.IlgiId;

                    if (neededDict.TryGetValue(ilgiId, out var neededProcessor))
                    {
                        processors.Add(neededProcessor);
                    }
                    else if (savedDict.TryGetValue(ilgiId, out var savedProcessor))
                    {
                        processors.Add(savedProcessor);
                    }
                    else
                    {
                        _logger.LogWarning("No processor found for news item with ID {IlgiId}", ilgiId);
                        // Consider what to do when no processor is found:
                        // 1. Skip (current behavior)
                        // 2. Add a placeholder processor
                        // 3. Throw an exception
                    }
                }

                if (processors.Count > 0)
                {
                    _logger.LogInformation("Successfully processed {Count} files", processors.Count);
                    var merged = await MergeAudioFilesAsync(processors, _storageConfig.BasePath, fileType, cancellationToken);
                    
                    _logger.LogInformation("merged stream id is {fileName}", merged.fileName);
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

        private async Task<(int id, AudioProcessor Processor)> ProcessSavedContentAsync
        (
            int ilgiId, 
            AudioType fileType, 
            CancellationToken cancellationToken)
        {
            try
            {
                byte[] stream = await _mp3Repository.ReadFileFromDiskAsync(ilgiId, fileType, cancellationToken);
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
        private async Task ProcessSqlWithSemaphoreAsync(
            int id,
            string localPath,
            string language,
            CancellationToken cancellationToken)
        {
            try
            {
                await _semaphoreSql.WaitAsync(cancellationToken);
                try
                {
                    await SaveMetadataSqlAsync(id, localPath, language, cancellationToken);
                }
                finally
                {
                    _semaphoreSql.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SQL with semaphore for {Id} at SaveMetadataSqlAsync", id);
                throw;
            }
        }
        // Optimized version of ProcessContentAsync
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
                    StoreMetadataRedisAsync(id, text, localPath, StoragePathHelper.GetStorageKey(id), cancellationToken),
                    ProcessSqlWithSemaphoreAsync(id, StoragePathHelper.GetStorageKey(id), language, cancellationToken)
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

        // Optimized version of SaveLocallyAsync
        private async Task SaveLocallyAsync(AudioProcessor processor, string localPath, CancellationToken cancellationToken)
        {
            // Use FileOptions for better performance
            var fileOptions = FileOptions.Asynchronous | FileOptions.SequentialScan;
            // Directory.CreateDirectory(Path.GetDirectoryName(localPath)!); // Ensure path exists

            await using (var fileStream = new FileStream(
                localPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128*1024,
                fileOptions))
                {
                    await processor.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

            _logger.LogInformation("Saved file locally: {LocalPath}", localPath);
        }

        // Optimized version of UploadToCloudAsync
        private async Task UploadToCloudAsync(AudioProcessor processor, string fileName, CancellationToken cancellationToken)
        {
            if (_storageClient == null || string.IsNullOrEmpty(_bucketName))
            {
                _logger.LogWarning("Skipping cloud upload because GCS is not configured.");
                return;
            }
            // Use the new async method to get the stream efficiently
            await using var uploadStream = await processor.GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);

            // Use optimized upload options
            var uploadOptions = new UploadObjectOptions
            {
                ChunkSize = 262144, // 256KB chunks for better handling
                PredefinedAcl = PredefinedObjectAcl.PublicRead // Make accessible without authentication if needed
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

        private async Task SaveMetadataSqlAsync(int id, string localPath, string language, CancellationToken cancellationToken)
        {
            var mp3Dto = new Mp3Dto
            {
                FileId=id,
                FileUrl=localPath,
                Language=language
            };
            await _mp3Repository.SaveMp3MetaToSql(mp3Dto, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Saved metadata to SQL via EF for {Id}", id);
        }
        // Helper class for better semaphore management
        private class SemaphoreGuard : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private bool _isAcquired;

            public SemaphoreGuard(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public async Task WaitAsync(CancellationToken cancellationToken)
            {
                await _semaphore.WaitAsync(cancellationToken);
                _isAcquired = true;
            }

            public void Dispose()
            {
                if (_isAcquired)
                {
                    _semaphore.Release();
                    _isAcquired = false;
                }
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
        // Update the DisposeAsync method to use Dispose instead of DisposeAsync for StorageClient
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_storageClient != null)
                {
                    // StorageClient does not support DisposeAsync, so use Dispose
                    await Task.Run(() => _storageClient.Dispose());
                }
                _semaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing TtsManager resources");
            }
        }
        /*
        private async Task ProcessContentAsync(string text, Guid id, string bucketName, CancellationToken cancellationToken)
        {
            var fileName = $"speech_{id}.mp3";
            var localPath = Path.Combine(LocalSavePath, fileName);
            var tempFiles = new List<string>();

            try
            {
                // Split text into chunks if needed (e.g., by sentences or paragraphs)
                var textChunks = SplitTextIntoChunks(text);
                var chunkFiles = new List<string>();

                // Process each chunk
                foreach (var chunk in textChunks)
                {
                    var chunkFile = await ProcessChunkAsync(chunk, id, cancellationToken);
                    if (chunkFile != null)
                    {
                        chunkFiles.Add(chunkFile);
                        tempFiles.Add(chunkFile);
                    }
                }

                // Merge chunks if there are multiple
                if (chunkFiles.Count > 1)
                {
                    localPath = await MergeContentsAsync(
                        chunkFiles,
                        fileName,
                        breakAudioPath: Path.Combine(LocalSavePath, "break.mp3"),
                        headerAudioPath: Path.Combine(LocalSavePath, "header.mp3"),
                        insertBreakEvery: 2,
                        cancellationToken
                    );
                }
                else if (chunkFiles.Count == 1)
                {
                    localPath = chunkFiles[0];
                }

                // Upload final file and store metadata
                await Task.WhenAll(
                    UploadFinalToCloudAsync(localPath, bucketName, fileName, cancellationToken),
                    StoreMetadataAsync(id, text, localPath, bucketName, fileName, cancellationToken)
                );

                _logger.LogInformation("Processed and merged content {Id}: {FileName}", id, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content {Id}", id);
                throw;
            }
            finally
            {
                // Cleanup temporary files
                foreach (var tempFile in tempFiles)
                {
                    try
                    {
                        if (File.Exists(tempFile) && tempFile != localPath)
                        {
                            File.Delete(tempFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary file: {File}", tempFile);
                    }
                }
            }
        }

        private IEnumerable<string> SplitTextIntoChunks(string text, int maxChunkLength = 2000)
        {
            var sentences = text.Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim() + ".");

            var currentChunk = new StringBuilder();

            foreach (var sentence in sentences)
            {
                if (currentChunk.Length + sentence.Length > maxChunkLength)
                {
                    if (currentChunk.Length > 0)
                    {
                        yield return currentChunk.ToString();
                        currentChunk.Clear();
                    }
                    if (sentence.Length > maxChunkLength)
                    {
                        // Handle very long sentences by splitting them into words
                        var words = sentence.Split(' ');
                        foreach (var chunk in SplitIntoChunks(words, maxChunkLength))
                        {
                            yield return chunk;
                        }
                        continue;
                    }
                }
                if (currentChunk.Length > 0)
                {
                    currentChunk.Append(" ");
                }
                currentChunk.Append(sentence);
            }

            if (currentChunk.Length > 0)
            {
                yield return currentChunk.ToString();
            }
        }

        private IEnumerable<string> SplitIntoChunks(string[] words, int maxLength)
        {
            var chunk = new StringBuilder();

            foreach (var word in words)
            {
                if (chunk.Length + word.Length + 1 > maxLength && chunk.Length > 0)
                {
                    yield return chunk.ToString();
                    chunk.Clear();
                }
                if (chunk.Length > 0)
                {
                    chunk.Append(" ");
                }
                chunk.Append(word);
            }

            if (chunk.Length > 0)
            {
                yield return chunk.ToString();
            }
        }

        private async Task<string?> ProcessChunkAsync(string chunk, Guid id, string language, CancellationToken cancellationToken)
        {
            var chunkFileName = $"chunk_{Guid.NewGuid()}_{id}.mp3";
            var chunkPath = Path.Combine(LocalSavePath, chunkFileName);

            try
            {
                // Get the voice configuration for the specified language
                if (!_config.Value.Feed.TryGetValue(language, out var languageConfig))
                {
                    throw new InvalidOperationException($"No voice configuration found for language: {language}");
                }

                // Get the first available voice for the language
                var voiceId = languageConfig.Voices.First().Value;

                var voice = await _elevenLabsClient.VoicesEndpoint
                    .GetVoiceAsync(voiceId, false, cancellationToken);

                var request = await CreateTtsRequestAsync(voice, chunk);

                await using var voiceClip = await _elevenLabsClient.TextToSpeechEndpoint
                    .TextToSpeechAsync(request, null, cancellationToken);

                await using var audioProcessor = new AudioProcessor(voiceClip);
                await using var fileStream = File.Create(chunkPath);
                await audioProcessor.CopyToAsync(fileStream, cancellationToken);

                return chunkPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chunk for content {Id}", id);
                return null;
            }
        }

        private async Task UploadFinalToCloudAsync(string localPath, string bucketName, string fileName, CancellationToken cancellationToken)
        {
            await using var fileStream = File.OpenRead(localPath);
            await _storageClient.UploadObjectAsync(
                bucketName,
                fileName,
                "audio/mpeg",
                fileStream,
                cancellationToken: cancellationToken);
        }
        */
        /* ElevenLabs.VoiceClip elevenLabsVoiceClip = await _elevenLabsClient.TextToSpeechEndpoint
            .TextToSpeechAsync(request, null, cancellationToken);

        // 2. Convert MP3 to M4A using the audio conversion service
        var m4aPath = await _audioConversionService
            .ConvertMp3ToM4aAsync(elevenLabsVoiceClip.ClipData.ToArray(), cancellationToken);

        // 3. Load converted audio into AudioProcessor
        var audioData = await File.ReadAllBytesAsync(m4aPath, cancellationToken);
        var voiceClip = new VoiceClip(audioData);
        var audioProcessor = new AudioProcessor(voiceClip);

        // 4. Save to desired local path (renaming the temp file)
        File.Move(m4aPath, localPath, overwrite: true);
        */
    }
}