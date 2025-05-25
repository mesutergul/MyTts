using Microsoft.AspNetCore.Mvc;
using MyTts.Models;
using MyTts.Repositories;
using MyTts.Services.Interfaces;
using MyTts.Helpers;
using System.Diagnostics;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using System.IO;
using System.Linq;

namespace MyTts.Services
{
    public class Mp3Service : IMp3Service, IAsyncDisposable
    {
        private readonly ILogger<Mp3Service> _logger;
        private readonly IMp3Repository _mp3FileRepository;
        private readonly ITtsClient _ttsClient;
        private readonly IRedisCacheService? _cache;
        private readonly ICache<int, string> _ozetCache;
        private readonly SemaphoreSlim _processingSemaphore;
        private const int MaxConcurrentProcessing = 1;
        private bool _disposed;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IGeminiTtsClient _geminiTtsClient;

        public Mp3Service(
            ILogger<Mp3Service> logger,
            IMp3Repository mp3FileRepository,
            ITtsClient ttsClient, // Existing ElevenLabs client
            IGeminiTtsClient geminiTtsClient, // New Gemini client
            IRedisCacheService cache,
            ICache<int, string> ozetCache,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mp3FileRepository = mp3FileRepository ?? throw new ArgumentNullException(nameof(mp3FileRepository));
            _ttsClient = ttsClient ?? throw new ArgumentNullException(nameof(ttsClient));
            _geminiTtsClient = geminiTtsClient ?? throw new ArgumentNullException(nameof(geminiTtsClient)); // Initialize here
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _processingSemaphore = new SemaphoreSlim(MaxConcurrentProcessing);
            _ozetCache = ozetCache;
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        }
        public async Task<string> CreateMultipleMp3Async(
            string language, // This should be a language code like "en-US" for Gemini
            int limit,
            AudioType fileType, // Ensure this is compatible with Gemini (e.g., MP3)
            CancellationToken cancellationToken)
        {
            await _processingSemaphore.WaitAsync(cancellationToken);
            try
            {
                var newsList = await GetNewsList(cancellationToken);
                if (!newsList.Any())
                {
                    _logger.LogWarning("No news items found to process.");
                    // Optionally, fallback to CSV or other sources like the original method
                    // newsList = CsvFileReader.ReadHaberSummariesFromCsv(...) 
                }

                // Filter newsList by limit if necessary
                if (limit > 0 && newsList.Count > limit)
                {
                    newsList = newsList.Take(limit).ToList();
                }

                var (neededNewsList, savedNewsList) = await checkNewsList(newsList, language, fileType, cancellationToken);

                _logger.LogInformation("Attempting to process {Count} news items with Gemini TTS.", neededNewsList.Count()); // Use Count() for IEnumerable

                var processedNewsMetadata = new List<Mp3Dto>();
                int geminiSuccessCount = 0;
                int geminiFailureCount = 0;

                foreach (var newsItem in neededNewsList)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Cancellation requested, stopping MP3 creation.");
                        break;
                    }

                    try
                    {
                        _logger.LogInformation("Processing news ID {NewsId} with Gemini: {Title}", newsItem.IlgiId, newsItem.Baslik);
                        // Assuming 'language' parameter is compatible with Gemini (e.g., "en-US")
                        // VoiceName can be null to use default from config, or specify one if API supports
                        Stream audioStream = await _geminiTtsClient.SynthesizeSpeechAsync(
                            newsItem.Ozet,
                            language,
                            null, // Or a specific voice/model name if available and configurable
                            cancellationToken);

                        if (audioStream != null && audioStream != Stream.Null && audioStream.Length > 0)
                        {
                            // Save the stream to a local file
                            var localPath = StoragePathHelper.GetFullPathById(newsItem.IlgiId, fileType); // Assuming fileType is Mp3
                            string? directory = Path.GetDirectoryName(localPath);
                            if (directory != null && !Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }

                            using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                            {
                                await audioStream.CopyToAsync(fileStream, cancellationToken);
                            }
                            await audioStream.DisposeAsync();
                            _logger.LogInformation("Successfully saved Gemini MP3 for news ID {NewsId} to {Path}", newsItem.IlgiId, localPath);

                            // Add to metadata for saving
                            var myHash = TextHasher.ComputeMd5Hash(newsItem.Ozet);
                            _ozetCache.Set(newsItem.IlgiId, myHash); // Keep existing cache logic
                            processedNewsMetadata.Add(new Mp3Dto
                            {
                                FileId = newsItem.IlgiId,
                                FileUrl = StoragePathHelper.GetStorageKey(newsItem.IlgiId), // Or localPath depending on what FileUrl represents
                                Language = language,
                                OzetHash = myHash,
                                FileType = fileType // Store the file type
                            });
                        }
                        else
                        {
                            _logger.LogWarning("Gemini TTS returned null or empty stream for news ID {NewsId} ({Title}).", newsItem.IlgiId, newsItem.Baslik);
                            geminiFailureCount++;
                        }
                    }
                    catch (OperationCanceledException opEx)
                    {
                        _logger.LogWarning(opEx, "Gemini TTS operation was canceled for news ID {NewsId} ({Title}). Skipping this item.", newsItem.IlgiId, newsItem.Baslik);
                        geminiFailureCount++;
                        // Re-throw if the main cancellationToken was triggered, otherwise continue
                        if (cancellationToken.IsCancellationRequested) throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing news ID {NewsId} ({Title}) with Gemini TTS. Skipping this item.", newsItem.IlgiId, newsItem.Baslik);
                        geminiFailureCount++;
                        // Optionally, decide if one failure should stop all, or continue (current: continues)
                    }
                }

                if (processedNewsMetadata.Any())
                {
                    _logger.LogInformation("Starting background SQL operations for {Count} successfully Gemini-processed files.", processedNewsMetadata.Count);
                    // Background task to save metadata
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            var repository = scope.ServiceProvider.GetRequiredService<IMp3Repository>();
                            await repository.SaveMp3MetadataToSqlBatchAsync(processedNewsMetadata, fileType, CancellationToken.None); // Use CancellationToken.None for fire-and-forget
                            _logger.LogInformation("Successfully saved metadata for {Count} Gemini-processed files in background.", processedNewsMetadata.Count);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving metadata in background for {Count} Gemini-processed files.", processedNewsMetadata.Count);
                        }
                    }); // No await, fire and forget
                }
                
                int attemptedCount = neededNewsList.Count(); // Total items attempted with Gemini
                geminiSuccessCount = processedNewsMetadata.Count; // Items successfully processed and saved by Gemini
                // geminiFailureCount is already calculated in the loop

                _logger.LogInformation(
                    "Gemini TTS processing summary for language {Language}: Attempted: {AttemptedCount}, Succeeded: {SucceededCount}, Failed: {FailedCount}",
                    language, attemptedCount, geminiSuccessCount, geminiFailureCount);

                // The original method calls _ttsClient.ProcessContentsAsync - this has been replaced by the Gemini-specific loop for 'neededNewsList'.
                // If 'savedNewsList' or other items still need processing by the old _ttsClient, that logic would need to be re-introduced or handled separately.
                // For now, this method primarily focuses on processing 'neededNewsList' with Gemini.

                return $"Gemini TTS processing for language '{language}': {attemptedCount} items attempted, {geminiSuccessCount} succeeded, {geminiFailureCount} failed.";
            }
            catch (OperationCanceledException opEx)
            {
                _logger.LogWarning(opEx, "CreateMultipleMp3Async operation was canceled.");
                // Rethrow to ensure the operation is marked as canceled for the caller
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred in CreateMultipleMp3Async (Gemini TTS path).");
                // Consider the overall status. If some items were processed, it might not be a total failure.
                // The return string will reflect 0 successes if it reaches here before any processing.
                return $"Processing completed with an unexpected error: {ex.Message}";
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        private async Task<(IEnumerable<HaberSummaryDto> neededNewsList, IEnumerable<HaberSummaryDto> savedNewsList)> checkNewsListInDB(List<HaberSummaryDto> newsList, AudioType fileType, CancellationToken cancellationToken)
        {
            var idList = newsList.Select(er => er.IlgiId).ToList();
            List<int> existings = await GetExistingMetaList(idList, cancellationToken);
            var neededNewsList = newsList
                            .Where(h => !idList.Contains(h.IlgiId))
                            .ToList();
            var savedNewsList = newsList
                            .Where(h => idList.Contains(h.IlgiId))
                            .ToList();
            return (neededNewsList, savedNewsList);
        }

        private async Task<(List<HaberSummaryDto> neededNewsList, List<HaberSummaryDto> savedNewsList)> checkNewsList(List<HaberSummaryDto> newsList, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            var neededNewsList = new List<HaberSummaryDto>();
            var savedNewsList = new List<HaberSummaryDto>();
           // var existingHashList = await _mp3FileRepository.GetExistingHashList(newsList.Select(x => x.IlgiId).ToList(), cancellationToken);
            foreach (var news in newsList)
            {
                var existingHash = _ozetCache.Get(news.IlgiId);
                var isSame = existingHash == null || !TextHasher.HasTextChangedMd5(news.Ozet, existingHash);
                if (await _mp3FileRepository.FileExistsAnywhereAsync(news.IlgiId, language, fileType, cancellationToken) && isSame)
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
            return _cache != null
                ? await _cache.GetAsync<IEnumerable<Mp3Dto>>(cacheKey) ?? Enumerable.Empty<Mp3Dto>()
                : Enumerable.Empty<Mp3Dto>();
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
                FileShare.Read,
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
                var cachedPath = _cache != null ? await _cache.GetAsync<string>(cacheKey) : null;

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
                    FileShare.Read,
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
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _processingSemaphore.Dispose();
            if (_ttsClient is IAsyncDisposable disposableTtsClient)
            {
                await disposableTtsClient.DisposeAsync();
            }
            // Also dispose Gemini client if it's IAsyncDisposable
            if (_geminiTtsClient is IAsyncDisposable disposableGeminiClient)
            {
                await disposableGeminiClient.DisposeAsync();
            }
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
    }
}
