using System.Diagnostics;
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
using MyTts.Storage;
using MyTts.Data.Entities;
using MyTts.Repositories;
using MyTts.Models;

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
        //private readonly IAudioConversionService _audioConversionService;
        //private readonly Mp3MetaRepository? _mp3MetaRepository;
        private readonly IMp3Repository _mp3Repository;
        public const string LocalSavePath = "audio";
        private const int MaxConcurrentOperations = 20;

        public TtsManagerService(
            ElevenLabsClient elevenLabsClient,
            IOptions<ElevenLabsConfig> config,
            IOptions<StorageConfiguration> storageConfig,
            IMp3Repository mp3Repository,
            IRedisCacheService cache,
            IMp3StreamMerger mp3StreamMerger,
            //IAudioConversionService audioConversionService,
            ILogger<TtsManagerService> logger)
        {
            _elevenLabsClient = elevenLabsClient ?? throw new ArgumentNullException(nameof(elevenLabsClient));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mp3StreamMerger = mp3StreamMerger ?? throw new ArgumentNullException(nameof(mp3StreamMerger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _mp3Repository = mp3Repository;
            //_audioConversionService = audioConversionService ?? throw new ArgumentNullException(nameof(audioConversionService));
            _semaphore = new SemaphoreSlim(MaxConcurrentOperations);
            _semaphoreSql = new SemaphoreSlim(1);
            _storageConfig = storageConfig.Value;
            // Initialize Google Cloud Storage
            var gcs = InitializeGoogleCloudStorage(_storageConfig);
            if (gcs != null)
            {
                _storageClient = gcs.Value.client;
                _bucketName = gcs.Value.bucketName;
            }
            else
            {
                _storageClient = null;
                _bucketName = null;
            }
            // Initialize JSON options once
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            Directory.CreateDirectory(LocalSavePath);
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
        private async Task<(Stream audioData, string contentType, string fileName)> MergeAudioFilesAsync(List<AudioProcessor> processors, string basePath, AudioType fileType, CancellationToken cancellationToken = default)
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

            // Use the optimized Mp3StreamMerger
            return await _mp3StreamMerger.MergeMp3ByteArraysAsync(processors, basePath, fileType, cancellationToken);
        }
        // Helper method for single file scenario
        private async Task<(Stream audioData, string contentType, string fileName)> CreateSingleFileResultAsync(AudioProcessor processor, CancellationToken cancellationToken)
        {
            await using var uploadStream = await processor.GetStreamForCloudUploadAsync(cancellationToken);

            return (uploadStream, "audio/mpeg", $"single_{Guid.NewGuid()}.mp3");
        }
        // Optimized ProcessContentsAsync
        public async Task<(Stream audioData, string contentType, string fileName)> ProcessContentsAsync(
        IEnumerable<HaberSummaryDto> allNews, IEnumerable<HaberSummaryDto> neededNews, string language, AudioType fileType, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(neededNews);
            var allNewsList = allNews.ToList();
            var contentsNeededList = neededNews.ToList(); // Materialize once to avoid multiple enumeration
            var referenceIds = contentsNeededList.Select(p => p.IlgiId).ToHashSet();
            var contentsSavedList = allNews
                .Where(p => !referenceIds.Contains(p.IlgiId))
                .ToList();
            if (!contentsNeededList.Any() && !contentsSavedList.Any())
            {
                return (null, "", "");
            }

            try
            {
                var processingNeededTasks = new List<Task<(string LocalPath, AudioProcessor Processor)>>(contentsNeededList.Count);
                var processingSavedTasks = new List<Task<(string LocalPath, AudioProcessor Processor)>>(contentsSavedList.Count);
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
                    if (int.TryParse(result.LocalPath, out int ilgiId))
                    {
                        neededDict[ilgiId] = result.Processor;
                    }
                }
                foreach (var result in resultsSaved)
                {
                    if (int.TryParse(result.LocalPath, out int ilgiId))
                    {
                        savedDict[ilgiId] = result.Processor;
                    }
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
                    
                    //int.TryParse(merged.fileName.Substring(10, 9), out var mergedId);
                   // _logger.LogInformation("Merged file ID is {Id}", mergedId);
                   // await ProcessSqlWithSemaphoreAsync(mergedId, merged.fileName, "tr", cancellationToken);
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

        private async Task<(string LocalPath, AudioProcessor Processor)> ProcessSavedContentAsync(int ilgiId, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                byte[] stream = await _mp3Repository.ReadFileFromDiskAsync(ilgiId, fileType, cancellationToken);
                var voiceClip = new VoiceClip(stream);
                var audioProcessor = new AudioProcessor(voiceClip);
                return (ilgiId.ToString(), audioProcessor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing saved content {Id}", ilgiId);
                throw;
            }
        }

        private async Task<(string LocalPath, AudioProcessor Processor)> ProcessContentWithSemaphoreAsync(
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
        public async Task<(string LocalPath, AudioProcessor FileData)> ProcessContentAsync(
            string text, int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            var fileName = $"speech_{id}.{fileType.ToString().ToLower()}"; // m4a container for AAC
            var localPath = Path.Combine(LocalSavePath, fileName);

            try
            {
                // var voice = Voice.Arnold;
                // Fetch voice and create request in parallel if CreateTtsRequestAsync doesn't depend on voice
                var voiceTask = _elevenLabsClient.VoicesEndpoint
                    .GetVoiceAsync(_config.Value.VoiceId, withSettings: false, cancellationToken);

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
                    UploadToCloudAsync(audioProcessor, fileName, cancellationToken),
                    StoreMetadataRedisAsync(id, text, localPath, fileName, cancellationToken),
                    ProcessSqlWithSemaphoreAsync(id, fileName, language, cancellationToken)
                };

                await Task.WhenAll(tasks);

                _logger.LogInformation("Processed content {Id}: {FileName}", id, fileName);
                return (localPath, audioProcessor);
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
            if (_cache == null && await _cache!.IsConnectedAsync()) return;
            var metadata = new AudioMetadata
            {
                Id = id,
                Text = text,
                LocalPath = localPath,
                GcsPath = $"gs://{_bucketName}/{fileName}",
                Timestamp = DateTime.UtcNow
            };

            await _cache!.SetAsync<AudioMetadata>($"tts:{id}", metadata, TimeSpan.FromHours(1));

        }

        private async Task SaveMetadataSqlAsync(int id, string localPath, string language, CancellationToken cancellationToken)
        {
            var mp3Meta = new Mp3Meta
            {
                FileId=id,
                FileUrl=localPath,
                Language=language
            };
            await _mp3Repository.SaveMp3MetaToSql(mp3Meta, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Saved metadata to SQL via EF for {Id}", id);
        }

        public async Task<string> MergeContentsAsync(
            IEnumerable<string> audioFiles,
            string outputFileName,
            string? breakAudioPath = null,
            string? headerAudioPath = null,
            int insertBreakEvery = 0,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audioFiles);
            ArgumentException.ThrowIfNullOrEmpty(outputFileName);

            var outputPath = Path.Combine(LocalSavePath, outputFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? string.Empty);

            try
            {
                var fileList = audioFiles.ToList();

                if (!string.IsNullOrEmpty(breakAudioPath))
                {
                    fileList = InsertBreakFile(fileList, breakAudioPath, insertBreakEvery);
                }

                if (!string.IsNullOrEmpty(headerAudioPath))
                {
                    fileList.Insert(0, headerAudioPath);
                }

                var success = await MergeFilesWithFfmpeg(fileList, outputPath, cancellationToken);

                if (!success)
                {
                    throw new InvalidOperationException("Failed to merge audio files");
                }

                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error merging audio files to: {OutputPath}", outputPath);
                throw;
            }
        }

        private List<string> InsertBreakFile(List<string> files, string breakFile, int insertEvery)
        {
            if (insertEvery <= 0) return files;

            var result = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                result.Add(files[i]);
                if ((i + 1) % insertEvery == 0 && i < files.Count - 1)
                {
                    result.Add(breakFile);
                }
            }
            return result;
        }

        private async Task<bool> MergeFilesWithFfmpeg(
            List<string> files,
            string outputFilePath,
            CancellationToken cancellationToken)
        {
            try
            {
                foreach (var file in files)
                {
                    if (!await ValidateAudioStream(file))
                    {
                        _logger.LogError("Invalid or no audio stream found in file: {File}", file);
                        return false;
                    }
                }

                var tempFileList = Path.GetTempFileName();
                var fileListContent = string.Join("\n", files.Select(f => $"file '{f}'"));
                await File.WriteAllTextAsync(tempFileList, fileListContent, cancellationToken);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-f concat -safe 0 -i \"{tempFileList}\" -c copy \"{outputFilePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(cancellationToken);

                var output = await outputTask;
                var error = await errorTask;

                _logger.LogInformation("FFmpeg Output: {Output}", output);
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogError("FFmpeg Error: {Error}", error);
                }

                File.Delete(tempFileList);
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error merging audio files with FFmpeg");
                throw;
            }
        }

        private async Task<bool> ValidateAudioStream(string filePath)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffprobe",
                        Arguments = $"-i \"{filePath}\" -show_streams -select_streams a -of json",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0) return false;

                var streamInfo = JsonSerializer.Deserialize<FFprobeOutput>(output);
                return streamInfo?.Streams?.Any(s => s.CodecType == "audio") ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate audio stream for file: {File}", filePath);
                return false;
            }
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
        private record FFprobeOutput
        {
            public List<StreamInfo>? Streams { get; init; }
        }
        private class StreamInfo
        {
            [JsonPropertyName("codec_type")]
            public string? CodecType { get; set; }
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

        private async Task<string?> ProcessChunkAsync(string chunk, Guid id, CancellationToken cancellationToken)
        {
            var chunkFileName = $"chunk_{Guid.NewGuid()}_{id}.mp3";
            var chunkPath = Path.Combine(TempPath, chunkFileName);

            try
            {
                var voice = await _elevenLabsClient.VoicesEndpoint
                    .GetVoiceAsync(_config.Value.VoiceId, false, cancellationToken);

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