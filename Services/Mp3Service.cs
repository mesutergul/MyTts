using Microsoft.AspNetCore.Mvc;
using MyTts.Models;
using MyTts.Repositories;
using MyTts.Services.Interfaces;
using MyTts.Helpers;
using System.Diagnostics;
using MyTts.Services.Constants;
using System.Collections.Concurrent;

namespace MyTts.Services
{
    public class Mp3Service : IMp3Service, IAsyncDisposable
    {
        private readonly ILogger<Mp3Service> _logger;
        private readonly IMp3Repository _mp3FileRepository;
        private readonly ITtsClient _ttsClient;
        private readonly IRedisCacheService _cache;
        private readonly SemaphoreSlim _processingSemaphore;
        private const int MaxConcurrentProcessing = 1;
        private bool _disposed;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private const string HASH_CACHE_KEY_PREFIX = "hash:";
        private static readonly TimeSpan HASH_CACHE_DURATION = TimeSpan.FromDays(7);
        private readonly ConcurrentDictionary<int, Task> _processingTasks = new();
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancellationTokens = new();

        public Mp3Service(
            ILogger<Mp3Service> logger,
            IMp3Repository mp3FileRepository,
            ITtsClient ttsClient,
            IRedisCacheService cache,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mp3FileRepository = mp3FileRepository ?? throw new ArgumentNullException(nameof(mp3FileRepository));
            _ttsClient = ttsClient ?? throw new ArgumentNullException(nameof(ttsClient));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _processingSemaphore = new SemaphoreSlim(MaxConcurrentProcessing);
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        }
        public async Task<string> CreateMultipleMp3Async(
            string language,
            int limit,
            AudioType fileType,
            CancellationToken cancellationToken)
        {
            await _processingSemaphore.WaitAsync(cancellationToken);
            try
            {
                var newsList = await GetNewsList(cancellationToken);
                var simdi = DateTime.Now;
                var anchorText = TurkishDateTimeText.GunlukSeslendirmeMetniOlustur(simdi);
                var anchor = new HaberSummaryDto()
                {
                    Baslik = "Seslendirme",
                    IlgiId = TurkishDateTimeText.GetCompactTimeId(simdi),
                    Ozet = anchorText
                };
                newsList.Insert(0, anchor);
                var (neededNewsList, savedNewsList) = await FilterNewsListAsync(newsList, language, fileType, cancellationToken);
                // Process needed news in parallel
                await _ttsClient.ProcessContentsAsync(newsList, neededNewsList, savedNewsList, language, fileType, cancellationToken);

                // Start SQL operations as fire-and-forget

                var metadataList = newsList.Select(async news =>
                {
                    var myHash = TextHasher.ComputeMd5Hash(news.Ozet);
                    await SetHashToCacheAsync(news.IlgiId, myHash, cancellationToken);
                    _logger.LogInformation("Hash for {Id}: {Hash}", news.IlgiId, myHash);
                    return new Mp3Dto
                    {
                        FileId = news.IlgiId,
                        FileUrl = StoragePathHelper.GetFullPathById(news.IlgiId, fileType),
                        Language = language,
                        OzetHash = myHash
                    };
                }).ToList();

                _logger.LogInformation("Starting background SQL operations for {Count} files", metadataList.Count);

                // Create a copy of the metadata list for the background task
                var metadataCopy = metadataList.Select(x => x.Result).ToList();

                // Create a new scope for the background task
                _ = Task.Run(async () =>
                {
                    IServiceScope? scope = null;
                    try
                    {
                        scope = _serviceScopeFactory.CreateScope();
                        var repository = scope.ServiceProvider.GetRequiredService<IMp3Repository>();

                        _logger.LogInformation("Background SQL operation started for {Count} files", metadataCopy.Count);

                        // Split the metadata into smaller chunks to reduce transaction time
                        const int chunkSize = 5;
                        for (int i = 0; i < metadataCopy.Count; i += chunkSize)
                        {
                            var chunk = metadataCopy.Skip(i).Take(chunkSize).ToList();
                            try
                            {
                                await repository.SaveMp3MetadataToSqlBatchAsync(chunk, AudioType.Mp3, CancellationToken.None);
                                _logger.LogInformation("Successfully saved chunk {ChunkNumber} of {TotalChunks} in background",
                                    (i / chunkSize) + 1,
                                    (int)Math.Ceiling(metadataCopy.Count / (double)chunkSize));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error saving chunk {ChunkNumber} of {TotalChunks} in background",
                                    (i / chunkSize) + 1,
                                    (int)Math.Ceiling(metadataCopy.Count / (double)chunkSize));
                            }
                        }

                        _logger.LogInformation("Successfully saved all metadata for {Count} files in background", metadataCopy.Count);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Background SQL operation was cancelled");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error saving metadata in background for {Count} files", metadataCopy.Count);
                    }
                    finally
                    {
                        if (scope != null)
                        {
                            if (scope is IAsyncDisposable asyncDisposable)
                            {
                                await asyncDisposable.DisposeAsync();
                            }
                            else
                            {
                                scope.Dispose();
                            }
                        }
                    }
                }, CancellationToken.None); // Use CancellationToken.None for background task



                return "Processing completed successfully";
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        private async Task<(List<HaberSummaryDto> neededNewsList, List<HaberSummaryDto> savedNewsList)> FilterNewsListAsync(
            List<HaberSummaryDto> newsList,
            string language,
            AudioType fileType,
            CancellationToken cancellationToken)
        {
            var neededNewsList = new List<HaberSummaryDto>();
            var savedNewsList = new List<HaberSummaryDto>();
            var existingHashList = new Dictionary<int, string>();

            // Get all hashes from Redis in one batch
            var hashKeys = newsList.Select(x => GetHashCacheKey(x.IlgiId)).ToList();
            var cachedHashes = await _cache.GetAsync<Dictionary<string, string>>("hash:batch", cancellationToken);

            if (cachedHashes == null || !cachedHashes.Any())
            {
                existingHashList = await _mp3FileRepository.GetExistingHashList(newsList.Select(x => x.IlgiId).ToList(), cancellationToken);
                await SetHashRangeToCacheAsync(existingHashList, cancellationToken);
            }

            foreach (var news in newsList)
            {
                var existingHash = await GetHashFromCacheAsync(news.IlgiId, cancellationToken);
                var isSame = existingHash == null || !TextHasher.HasTextChangedMd5(news.Ozet, existingHash);
                string redisKey = RedisKeys.FormatKey(RedisKeys.TTS_INDIVIDUAL_MP3_KEY, news.IlgiId);
                var inRedis = await _cache.ExistsAsync(redisKey, cancellationToken);

                if ((await _mp3FileRepository.FileExistsAnywhereAsync(news.IlgiId, language, fileType, cancellationToken) || inRedis) && isSame)
                {
                    savedNewsList.Add(news);
                }
                else
                {
                    neededNewsList.Add(news);
                }
            }
            return (neededNewsList, savedNewsList);
        }
        public async Task<Stream> CreateSingleMp3Async(OneRequest request, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                NewsDto news = await _mp3FileRepository.LoadNewsAsync(request.News, cancellationToken);
                if (string.IsNullOrEmpty(news.Summary))
                {
                    throw new InvalidOperationException($"News summary is null or empty for ID: {news.Id}");
                }
                var stream = await RequestSingleMp3Async(news.Id, news.Summary, request.Language, fileType, cancellationToken);

                // Save metadata after successful processing
                await SaveMp3MetadataAsync(
                    news.Id,
                    StoragePathHelper.GetStorageKey(news.Id),
                    request.Language,
                    cancellationToken);

                return stream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating single MP3 file");
                throw;
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }
        public async Task<Stream> RequestSingleMp3Async(int id, string content, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                await _processingSemaphore.WaitAsync();
                var (filePath, processor) = await _ttsClient.ProcessContentAsync(content, id, language, fileType, cancellationToken);
                return await processor.GetStreamForCloudUploadAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing single MP3 file");
                throw;
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }
        public async Task<IEnumerable<Mp3Dto>> GetFeedByLanguageAsync(ListRequest listRequest, CancellationToken cancellationToken)
        {
            var cacheKey = $"feed_{listRequest.Language}";
            return await _cache.GetAsync<IEnumerable<Mp3Dto>>(cacheKey) ?? Enumerable.Empty<Mp3Dto>();
        }
        public async Task<Mp3Dto> GetMp3FileAsync(int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.LoadMp3MetaByNewsIdAsync(id, fileType, cancellationToken)
                ?? throw new KeyNotFoundException($"MP3 file not found for ID: {id}");
        }
        public async Task<Mp3Dto> GetLastMp3ByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(language);
            return await _mp3FileRepository.LoadLatestMp3MetaByLanguageAsync(language, fileType, cancellationToken);
        }
        // Fix FileStream properties in all methods
        private FileStreamResult CreateFileStreamResponse(
            Stream stream,
            string fileName,
            Dictionary<string, string> headers,
            int bufferSize = 81920)
        {
            var result = new FileStreamResult(stream, "audio/mp4")
            {
                EnableRangeProcessing = true,
                FileDownloadName = fileName
            };

            // Create a Microsoft.AspNetCore.Http.IHeaderDictionary from the result
            var responseHeaders = new HeaderDictionary();
            foreach (var header in headers)
            {
                responseHeaders[header.Key] = header.Value;
            }

            // Set the headers using the ResponseHeaders property
            foreach (var header in headers)
            {
                switch (header.Key.ToLower())
                {
                    case "content-disposition":
                        // Content-Disposition is handled by FileDownloadName
                        continue;
                    case "content-type":
                        // Content-Type is handled by the constructor
                        continue;
                    default:
                        responseHeaders.Add(header.Key, header.Value);
                        break;
                }
            }
            return result;
        }
        private ObjectResult CreateErrorResponse(Exception ex, string message)
        {
            return new ObjectResult(new { message, error = ex.Message })
            {
                StatusCode = 500
            };
        }
        private Stream CreateFileStream(string filePath, bool isStreaming = false)
        {
            return new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: isStreaming ? 81920 : 4096,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan
            );
        }
        // Update file path handling
        private string GetAudioFilePath(string fileName)
        {
            return StoragePathHelper.GetFullPath(fileName, AudioType.Mp3);
        }
        public async Task<bool> FileExistsAnywhereAsync(int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.FileExistsAnywhereAsync(id, language, fileType, cancellationToken);
        }
        public async Task<IActionResult> StreamMp3(int id, string language, AudioType fileType, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!await FileExistsAnywhereAsync(id, language, fileType, cancellationToken))
                {
                    _logger.LogWarning("MP3 file metadata not found for ID: {Id}", id);
                    return new NotFoundObjectResult(new { message = "MP3 file not found." });
                }
                var mp3File = await _mp3FileRepository.GetFromCacheAsync($"mp3stream:{id}", cancellationToken)
                    ?? await _mp3FileRepository.LoadMp3MetaByNewsIdAsync(id, fileType, cancellationToken);
                string filePath = GetAudioFilePath(mp3File.FileUrl);
                /// Verifies physical file exists
                if (!await _mp3FileRepository.Mp3FileExistsAsync(id, fileType, cancellationToken))
                {
                    _logger.LogWarning("MP3 file not found at path: {Path}", filePath);
                    return new NotFoundObjectResult(new { message = "MP3 file not found." });
                }
                /// Creates optimized FileStream for audio streaming
                var stream = CreateFileStream(filePath, isStreaming: true);
                /// Sets up proper HTTP headers for streaming 
                /// Includes caching, range support
                var headers = CreateStandardHeaders(mp3File.FileUrl);

                return CreateFileStreamResponse(stream, mp3File.FileUrl, headers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming MP3 for ID: {Id}", id);
                return CreateErrorResponse(ex, "An error occurred while streaming the audio.");
            }
        }
        private async Task<Mp3Dto> GetOrLoadMp3File(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            var cacheKey = $"mp3stream:{id}";
            return await _mp3FileRepository.GetFromCacheAsync(cacheKey, cancellationToken)
                ?? await _mp3FileRepository.LoadAndCacheMp3File(id, fileType, cancellationToken);
        }
        public async Task<byte[]> GetMp3FileBytes(int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.LoadMp3FileAsync(id, language, fileType, cancellationToken);
        }
        public async Task<Stream> GetAudioFileStream(int id, string language, AudioType fileType, bool isMerged, CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.ReadLargeFileAsStreamAsync(id, language, 81920, fileType, isMerged, cancellationToken);
        }
        private async Task<(Stream FileData, string LocalPath)> GetOrProcessMp3File(int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            var fileData = await _mp3FileRepository.LoadMp3FileAsync(id, language, fileType, cancellationToken);
            int localPath = 0;
            Stream? fileStream;
            if (fileData == null || fileData.Length == 0)
            {
                var content = "Olanlar daha dün gibi taze.";//_newsFeedsService.GetFeedUrl("tr");
                (localPath, var processor) = await _ttsClient.ProcessContentAsync(content, id, language, fileType, cancellationToken);
                fileStream = await processor.GetStreamForCloudUploadAsync(cancellationToken);
            }
            else fileStream = new MemoryStream(fileData);
            return (fileStream, localPath.ToString());
        }
        private IActionResult CreateStreamResponse(string filePath, string fileName)
        {
            var stream = CreateFileStream(filePath, isStreaming: true);
            var headers = CreateStandardHeaders(fileName);
            return CreateFileStreamResponse(stream, fileName, headers);
        }
        private IActionResult CreateDownloadResponse(byte[] fileData, string localPath)
        {
            return new FileContentResult(fileData, "audio/mp4")
            {
                FileDownloadName = Path.GetFileName(localPath),
                EnableRangeProcessing = true
            };
        }
        private NotFoundObjectResult CreateNotFoundResponse(string message)
        {
            return new NotFoundObjectResult(new { message });
        }
        public async Task<IActionResult> DownloadMp3(int id, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                var (fileStream, localPath) = await GetOrProcessMp3File(id, language, fileType, cancellationToken);
                return CreateStreaming(fileStream, localPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MP3 download for ID: {Id}", id);
                return CreateErrorResponse(ex, "An error occurred while processing the request.");
            }
        }
        private IActionResult CreateStreaming(Stream fileStream, string localPath)
        {
            var headers = CreateStandardHeaders(localPath);
            return CreateFileStreamResponse(fileStream, localPath, headers);
        }
        public async Task<IActionResult> DownloadMp3FromDisk(int id, AudioType fileType, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check cache first
                string cacheKey = $"mp3disk:{id}";
                var cachedPath = await _cache.GetAsync<string>(cacheKey);

                string filePath;
                if (cachedPath != null)
                {
                    filePath = cachedPath;
                }
                else
                {
                    // Get file metadata from repository
                    var mp3File = await _mp3FileRepository.LoadMp3MetaByNewsIdAsync(id, fileType, cancellationToken);
                    if (mp3File == null)
                    {
                        _logger.LogWarning("MP3 file metadata not found for ID: {Id}", id);
                        return new NotFoundObjectResult(new { message = "MP3 file not found." });
                    }

                    filePath = StoragePathHelper.GetFullPath(mp3File.FileUrl, fileType);

                    // Cache the path for future requests
                    await _cache!.SetAsync(cacheKey, filePath, TimeSpan.FromHours(1));
                }

                // Check if file exists
                if (!await _mp3FileRepository.Mp3FileExistsAsync(id, fileType, cancellationToken))
                {
                    _logger.LogWarning("MP3 file not found at path: {Path}", filePath);
                    return new NotFoundObjectResult(new { message = "MP3 file not found." });
                }

                // Set response headers for better caching and streaming
                var headers = new Dictionary<string, string>
                {
                    { "Content-Disposition", $"attachment; filename=\"{Path.GetFileName(filePath)}\"" },
                    { "Cache-Control", "public, max-age=3600" },
                    { "Accept-Ranges", "bytes" }
                };

                // Create FileStreamResult with proper settings
                var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    4096, // Default buffer size
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                );

                return new FileStreamResult(stream, "audio/mp4")
                {
                    FileDownloadName = Path.GetFileName(filePath),
                    EnableRangeProcessing = true // Enables partial content requests
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading MP3 file from disk for ID: {Id}", id);
                return new ObjectResult(new
                {
                    message = "An error occurred while processing the request.",
                    error = ex.Message
                })
                {
                    StatusCode = 500
                };
            }
        }
        private Dictionary<string, string> CreateStandardHeaders(string fileName)
        {
            return new Dictionary<string, string>
            {
                // Content-Disposition is handled by FileDownloadName
                { "Cache-Control", "public, max-age=3600" },
                { "Accept-Ranges", "bytes" },
                // Content-Type is handled by FileStreamResult constructor
                { "X-Content-Type-Options", "nosniff" }
            };
        }
        public async Task<IEnumerable<Mp3Dto>> GetMp3FileListAsync(string language, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(language);
            var mp3Files = await _mp3FileRepository.LoadListMp3MetadatasAsync(fileType, cancellationToken);
            return mp3Files.Where(f => f.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
        }
        public async Task<IEnumerable<Mp3Dto>> GetMp3FileListByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(language);
            var mp3Files = await _mp3FileRepository.LoadListMp3MetadatasAsync(fileType, cancellationToken);
            return mp3Files.Where(f => f.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
        }
        /// ffmpeg -i input.mp3 -c:a aac -b:a 128k output.m4a
        public async Task ConvertMp3ToM4A(string inputPath, string outputPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputPath}\" -c:a aac -b:a 128k \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg process");
            await process.WaitForExitAsync();
        }
        public async Task<List<HaberSummaryDto>> GetNewsList(CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.GetNewsList(cancellationToken);
        }
        public async Task<List<int>> GetExistingMetaList(List<int> myList, CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.GetExistingMetaList(myList, cancellationToken);
        }
        public async Task SaveMp3MetadataAsync(int id, string localPath, string language, CancellationToken cancellationToken)
        {
            try
            {
                var mp3Dto = new Mp3Dto
                {
                    FileId = id,
                    FileUrl = localPath,
                    Language = language
                };
                await _mp3FileRepository.SaveMp3MetaToSql(mp3Dto, cancellationToken);
                _logger.LogDebug("Saved metadata to SQL for ID {Id}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save metadata to SQL for ID {Id}", id);
                throw;
            }
        }
        public async Task SaveMp3MetadataBatchAsync(List<Mp3Dto> metadataList, CancellationToken cancellationToken)
        {
            try
            {
                // Save metadata to SQL in batch
                await _mp3FileRepository.SaveMp3MetadataToSqlBatchAsync(metadataList, AudioType.Mp3, cancellationToken);
                _logger.LogDebug("Saved batch metadata to SQL for {Count} files", metadataList.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save batch metadata for {Count} files", metadataList.Count);
                throw;
            }
        }
        private async Task SaveMp3MetadataBatchAsyncInternal(List<Mp3Dto> metadataList, CancellationToken cancellationToken)
        {
            try
            {
                // Save metadata to SQL in batch
                await _mp3FileRepository.SaveMp3MetadataToSqlBatchAsync(metadataList, AudioType.Mp3, cancellationToken);
                _logger.LogDebug("Saved batch metadata to SQL for {Count} files", metadataList.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save batch metadata for {Count} files", metadataList.Count);
                throw;
            }
        }
        private string GetHashCacheKey(int id) => $"{HASH_CACHE_KEY_PREFIX}{id}";
        private async Task<string?> GetHashFromCacheAsync(int id, CancellationToken cancellationToken)
        {
            return await _cache.GetAsync<string>(GetHashCacheKey(id), cancellationToken);
        }
        private async Task SetHashToCacheAsync(int id, string hash, CancellationToken cancellationToken)
        {
            await _cache.SetAsync(GetHashCacheKey(id), hash, HASH_CACHE_DURATION, cancellationToken);
        }
        private async Task SetHashRangeToCacheAsync(Dictionary<int, string> hashList, CancellationToken cancellationToken)
        {
            foreach (var (id, hash) in hashList)
            {
                await SetHashToCacheAsync(id, hash, cancellationToken);
            }
        }
        private async Task ProcessMp3Async(int id, string text, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting MP3 processing for ID: {Id}, Language: {Language}", id, language);

                // Create a new scope for notification
                using var scope = _serviceScopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                await notificationService.SendNotificationAsync(
                    "Processing Started",
                    $"Starting MP3 processing for ID: {id}, Language: {language}",
                    NotificationType.Info);

                var (filePath, processor) = await _ttsClient.ProcessContentAsync(text, id, language, fileType, cancellationToken);

                await notificationService.SendNotificationAsync(
                    "Processing Completed",
                    $"Successfully processed MP3 for ID: {id}, Language: {language}",
                    NotificationType.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MP3 for ID: {Id}, Language: {Language}", id, language);

                // Create a new scope for error notification
                using var scope = _serviceScopeFactory.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

                await notificationService.SendErrorNotificationAsync(
                    "Processing Failed",
                    $"Failed to process MP3 for ID: {id}, Language: {language}",
                    ex);
                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _processingSemaphore.Dispose();
            if (_ttsClient is IAsyncDisposable disposableTtsClient)
            {
                await disposableTtsClient.DisposeAsync();
            }
        }
    }
}
