using MyTts.Models;
using MyTts.Repositories;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using MyTts.Data.Entities;
using System.Threading.Tasks;

namespace MyTts.Services
{
    public class Mp3Service : IMp3Service, IAsyncDisposable
    {
        private readonly ILogger<Mp3Service> _logger;
        private readonly IMp3Repository _mp3FileRepository;
        private readonly ITtsManagerService _ttsManager;
        private readonly NewsFeedsService _newsFeedsService;
        private readonly IRedisCacheService? _cache;
        private const string LocalSavePath = "audio";
        private readonly SemaphoreSlim _processingSemaphore;
        private const int MaxConcurrentProcessing = 1;
        private bool _disposed;

        public Mp3Service(
            ILogger<Mp3Service> logger,
            IMp3Repository mp3FileRepository,
            ITtsManagerService ttsManager,
            IRedisCacheService cache,
            NewsFeedsService newsFeedsService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mp3FileRepository = mp3FileRepository ?? throw new ArgumentNullException(nameof(mp3FileRepository));
            _ttsManager = ttsManager ?? throw new ArgumentNullException(nameof(ttsManager));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _newsFeedsService = newsFeedsService ?? throw new ArgumentNullException(nameof(newsFeedsService));
            _processingSemaphore = new SemaphoreSlim(MaxConcurrentProcessing);
        }
        public async Task<(Stream audioData, string contentType, string fileName)> CreateMultipleMp3Async(
            string language, 
            int limit, 
            AudioType fileType, 
            CancellationToken cancellationToken)
        {
            try
            {
                await _processingSemaphore.WaitAsync();
                var newsList=await GetNewsList(cancellationToken);
           //     var neededNewsListByDB = checkNewsListInDB(newsList, fileType, cancellationToken);
                var neededNewsList = checkNewsList(newsList, fileType, cancellationToken);
                // var contents = await _newsFeedsService.GetFeedByLanguageAsync(language, limit);
                //var contents = new List<string>
                //{
                //    "May whatever is possible be done to reach an authentic, true and lasting peace as quickly as possible.",
                //    "Make sure you're awaiting all async operations properly, especially if you're using scoped services in async scenarios",
                //    "This is a common issue in ASP.NET Core applications, especially when working with services that need to maintain state or resources across asynchronous operations",
                //    "As the error message indicated, the API accepts only one method of authentication, not both simultaneously.",
                //    "I see the issue in your code. The problem is with how you're setting up the Authorization header. Let me fix that for you",
                //    "Routes are now grouped by functionality and follow a consistent pattern, making the code easier to read and maintain"
                // };
                return await _ttsManager.ProcessContentsAsync(newsList, neededNewsList, language, fileType);

            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        private async Task<IEnumerable<HaberSummaryDto>> checkNewsListInDB(List<HaberSummaryDto> newsList, AudioType fileType, CancellationToken cancellationToken)
        {
            var idList = newsList.Select(er=>er.IlgiId).ToList();
            List<int> existings = await GetExistingMetaList(idList, cancellationToken);
            var neededNewsList = newsList
                            .Where(h => !idList.Contains(h.IlgiId))
                            .ToList();
            return neededNewsList;
        }

        private List<HaberSummaryDto> checkNewsList(List<HaberSummaryDto> newsList, AudioType fileType, CancellationToken cancellationToken)
        {
            var neededNewsList = new List<HaberSummaryDto>();
            foreach (var news in newsList)
            {
                var fileName = $"speech_{news.IlgiId}.{fileType.ToString().ToLower()}"; // m4a container for AAC
                if (!File.Exists(Path.Combine(LocalSavePath, fileName)))
                {
                    neededNewsList.Add(news);
                }
            }
            return neededNewsList;
        }
        public async Task<Stream> CreateSingleMp3Async(OneRequest request, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
            News news = await _mp3FileRepository.LoadNewsAsync(request.News, cancellationToken);
               // var content = "May whatever is possible be done to reach an authentic, true and lasting peace as quickly as possible.";
                //await _newsFeedsService.GetFeedUrl(request.News);
               // var content = "Make sure you're awaiting all async operations properly, especially if you're using scoped services in async scenarios";
               // var content = "This is a common issue in ASP.NET Core applications, especially when working with services that need to maintain state or resources across asynchronous operations";
               //var content = "As the error message indicated, the API accepts only one method of authentication, not both simultaneously.";
             //   var content ="I see the issue in your code. The problem is with how you're setting up the Authorization header. Let me fix that for you";
              // var content = "Routes are now grouped by functionality and follow a consistent pattern, making the code easier to read and maintain";
                return await RequestSingleMp3Async(news.Id, news.Summary, request.Language, fileType, cancellationToken);
                // return await _mp3FileRepository.LoadMp3MetaByNewsIdAsync(request.News, fileType, cancellationToken)
                //     ?? throw new KeyNotFoundException($"MP3 file not found for ID: {request.News}");
                //await _processingSemaphore.WaitAsync();
                //var content = await _newsFeedsService.GetFeedUrl(request.News);
                // (filePath, processor) tu= await _ttsManager.ProcessContentAsync(content, Guid.NewGuid(), cancellationToken);
                //return await _mp3FileRepository.LoadMp3MetaByPathAsync(tu.filePath);
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
        public async Task<Stream> RequestSingleMp3Async(int id,string content, string language, AudioType fileType, CancellationToken cancellationToken)
        {
            try
            {
                await _processingSemaphore.WaitAsync();
                var (filePath, processor) = await _ttsManager.ProcessContentAsync(content, id, language, fileType, cancellationToken);
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
        /*
         await _processingSemaphore.WaitAsync();
                var content = await _newsFeedsService.GetFeedUrl(request.News);
                (string filePath, AudioProcessor processor) tu = await _ttsManager.ProcessContentAsync(content, Guid.NewGuid(), cancellationToken);
                return await tu.processor.GetStreamForCloudUpload(cancellationToken);

         */
        public async Task<IEnumerable<Mp3Meta>> GetFeedByLanguageAsync(ListRequest listRequest, CancellationToken cancellationToken)
        {
            var cacheKey = $"feed_{listRequest.Language}";
            return _cache != null
                ? await _cache.GetAsync<IEnumerable<Mp3Meta>>(cacheKey) ?? Enumerable.Empty<Mp3Meta>()
                : Enumerable.Empty<Mp3Meta>();
        }
        public async Task<Mp3Meta> GetMp3FileAsync(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.LoadMp3MetaByNewsIdAsync(id, fileType, cancellationToken)
                ?? throw new KeyNotFoundException($"MP3 file not found for ID: {id}");
        }

        public async Task<Mp3Meta> GetLastMp3ByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken)
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
        /// Creates an optimized FileStream with:
        /// Larger buffer(81920 bytes) for streaming
        ///	Asynchronous I/O
        ///	Sequential scan optimization
        ///	Read-only access
        ///	File sharing enabled
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
            return Path.Combine(LocalSavePath, fileName);
        }
        public async Task<bool> FileExistsAnywhereAsync(int id, AudioType fileType, CancellationToken cancellationToken) {
            return await _mp3FileRepository.FileExistsAnywhereAsync(id, fileType, cancellationToken);
        }
        /// <summary>
        /// Streams MP3 file by IDto the client, optimized for audio streaming with proper caching
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IActionResult> StreamMp3(int id, AudioType fileType, CancellationToken cancellationToken = default)
        {
            try
            {
                /// First tries to get the file metadata from cache
                /// If not in cache, loads and caches it from the repository
                //var mp3File = await _mp3FileRepository.GetFromCacheAsync<IMp3>($"mp3stream:{id}")
                //    ?? await _mp3FileRepository.LoadAndCacheMp3File<IMp3>(id, cancellationToken);

                if (!await FileExistsAnywhereAsync(id, fileType, cancellationToken))
                {
                    _logger.LogWarning("MP3 file metadata not found for ID: {Id}", id);
                    return new NotFoundObjectResult(new { message = "MP3 file not found." });
                }
                var mp3File = await _mp3FileRepository.GetFromCacheAsync($"mp3stream:{id}", cancellationToken)
                    ?? await _mp3FileRepository.LoadMp3MetaByNewsIdAsync(id, fileType, cancellationToken);
                string filePath = GetAudioFilePath(mp3File.FileUrl);
                /// Verifies physical file exists
                if (!await _mp3FileRepository.Mp4FileExistsAsync(filePath, fileType, cancellationToken))
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
        private async Task<Mp3Meta> GetOrLoadMp3File(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            var cacheKey = $"mp3stream:{id}";
            return await _mp3FileRepository.GetFromCacheAsync(cacheKey, cancellationToken)
                ?? await _mp3FileRepository.LoadAndCacheMp3File(id, fileType, cancellationToken);
        }
         public async Task<byte[]> GetMp3FileBytes(int id, AudioType fileType, CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.LoadMp3FileAsync(id, fileType, cancellationToken);
        }
        public async Task<Stream> GetAudioFileStream(int id, AudioType fileType, bool isMerged, CancellationToken cancellationToken)
        {
          return await _mp3FileRepository.ReadLargeFileAsStreamAsync(id, 81920, fileType, isMerged, cancellationToken);
        }
        /// <summary>
        /// Attempts to load existing file
        ///	If not found, creates new MP3 from content
        /// </summary>
        private async Task<(Stream FileData, string LocalPath)> GetOrProcessMp3File(int id, string language, AudioType fileType, CancellationToken cancellationToken)
        { 
            var fileData = await _mp3FileRepository.LoadMp3FileAsync(id, fileType, cancellationToken);
            string localPath="";
            Stream? fileStream = null;
            if (fileData == null || fileData.Length == 0)
            {
                var content = _newsFeedsService.GetFeedUrl("tr");
                (localPath, var processor)= await _ttsManager.ProcessContentAsync(content, id, language, fileType, cancellationToken);
                fileStream = await processor.GetStreamForCloudUploadAsync(cancellationToken);
            } else fileStream= new MemoryStream(fileData);
            return (fileStream, localPath);
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
        /// <summary>
        /// Download MP3 file by ID, if not found, process the content and creates a new MP3 file.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
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

        // public async Task<IActionResult> DownloadMp3(string id, CancellationToken cancellationToken)
        // {
        //     try
        //     {
        //         string localPath=""+id;
        //         byte[] fileData;
        //         fileData = await _mp3FileRepository.LoadMp3FileAsync(id);
        //         if (fileData == null || fileData.Length == 0)
        //         {
        //             var content =  _newsFeedsService.GetFeedUrl("id");
        //             localPath = await _ttsManager.ProcessContentAsync(content, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        //             fileData = await _mp3FileRepository.LoadMp3FileAsync(localPath);

        //         }
        //         // Set response headers
        //         var headers = new Dictionary<string, string>
        //         {
        //             { "Content-Disposition", $"attachment; filename=\"{Path.GetFileName(localPath)}\"" },
        //             { "Cache-Control", "public, max-age=3600" },
        //             { "Accept-Ranges", "bytes" }
        //         };

        //         // Return file result with proper headers
        //         return new FileContentResult(fileData, "audio/mpeg")
        //         {
        //             FileDownloadName = Path.GetFileName(localPath),
        //             EnableRangeProcessing = true
        //         };

        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogError(ex, "Error processing MP3 download for ID: {Id}", id);
        //         return new ObjectResult(new
        //         {
        //             message = "An error occurred while processing the request.",
        //             error = ex.Message
        //         })
        //         {
        //             StatusCode = 500
        //         };
        //     }
        // }
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

                    filePath = Path.Combine(LocalSavePath, mp3File.FileUrl);

                    // Cache the path for future requests
                    await _cache!.SetAsync(cacheKey, filePath, TimeSpan.FromHours(1));
                }

                // Check if file exists
                if (!await _mp3FileRepository.Mp4FileExistsAsync(filePath, fileType, cancellationToken))
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
            if (!_disposed)
            {
                _processingSemaphore.Dispose();
                _disposed = true;
            }
            await Task.CompletedTask;
        }

        public async Task<IEnumerable<Mp3Meta>> GetMp3FileListAsync(AudioType fileType, CancellationToken cancellationToken)
        {
            // return await _mp3FileRepository.LoadAllMp3MetaAsync();
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<Mp3Meta>> GetMp3FileListByLanguageAsync(string language, AudioType fileType, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(language);
            // return await _mp3FileRepository.LoadMp3MetaByLanguageAsync(language);
            throw new NotImplementedException();
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

            using var process = Process.Start(startInfo);
            await process.WaitForExitAsync();
        }

        public async Task<List<HaberSummaryDto>> GetNewsList(CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.GetNewsList(cancellationToken);
        }
        public async Task<List<int>> GetExistingMetaList(List<int> myList, CancellationToken cancellationToken)
        {
            return await _mp3FileRepository.GetExistingMetaList(myList,cancellationToken);
        }
    }
}
