using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using MyTts.Models;
using MyTts.Repositories;
using MyTts.Services;

namespace MyTts.Controllers
{
    public class Mp3Controller : ControllerBase
    {
        private readonly IMp3Service _mp3Service;
        private readonly ILogger<Mp3Controller> _logger;

        public Mp3Controller(IMp3Service mp3Service, ILogger<Mp3Controller> logger)
        {
            _mp3Service = mp3Service ?? throw new ArgumentNullException(nameof(mp3Service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        //Works Fine
        /// <summary>
        /// Creates multiple MP3 files based on the specified language and limit.   
        /// </summary>
        public async Task<IActionResult> Feed(HttpContext context, string language, int limit, CancellationToken cancellationToken)
        {
            try
            {
                await _mp3Service.CreateMultipleMp3Async(language, limit, AudioType.Mp3, cancellationToken);
                return Ok(language);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MP3 list request");
                return Problem("Failed to process MP3 list request");
            }
        }
        /// <summary>
        /// Creates a single MP3 file based on the specified request.
        /// </summary>
        public async Task<IActionResult> One(HttpContext context, OneRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _mp3Service.CreateSingleMp3Async(request, AudioType.M4a, cancellationToken);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing single MP3 request");
                return Problem("Failed to process MP3 request");
            }
        }


        /// <summary>
        /// Retrieves a list of MP3 files based on the specified request.
        /// </summary>
        public async Task<IActionResult> List(HttpContext context, ListRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var feed = await _mp3Service.GetFeedByLanguageAsync(request, cancellationToken);
                return Ok(feed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving feed for language {Language}", request.Language);
                return Problem("Failed to retrieve feed");
            }
        }
        /// <summary>
        /// It is responsible for retrieving an MP3 file by its unique identifier (id) 
        /// and returning it to the client as a downloadable file.
        /// </summary>
        public async Task GetFilem(HttpContext context, CancellationToken cancellationToken)
        {
            try
            {

                await using var fileStream = await _mp3Service.GetAudioFileStream(0, AudioType.Mp3, true, cancellationToken);

                if (fileStream == null || fileStream == Stream.Null)
                {
                    _logger.LogWarning("File not found with name merged. GetMp4File returned null or Stream.Null.");
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync($"File with name merged not found.", cancellationToken);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "audio/mpeg"; // "audio/mp4" or "audio/mpeg" for MP3
                // Use constants for header names for type safety and to avoid magic strings.
                context.Response.Headers[HeaderNames.CacheControl] = "no-cache"; // Or "public, max-age=xxxx" if caching is desired

                if (fileStream.CanSeek)
                {
                    context.Response.ContentLength = fileStream.Length;
                    // Ensure stream is at the beginning if its position was changed by checking Length.
                    // Most stream providers that give you Length will have Position at 0 if it's a fresh stream.
                    // fileStream.Position = 0;
                }
                await fileStream.CopyToAsync(context.Response.Body, cancellationToken);
            }
            catch (FileNotFoundException ex) // Catch a specific exception if your service throws it
            {
                _logger.LogWarning(ex, "File not found exception for merged");
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync($"File with name merged not found.", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "File not found exception for merged");
                if (!context.Response.HasStarted) // Check if headers have already been sent
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    // Avoid sending detailed error information to the client in production
                    await context.Response.WriteAsync("An error occurred while streaming the file.", cancellationToken);
                }
                // If response has started, the connection will likely be aborted, or client receives partial content.
            }
        }
        public async Task GetFile(HttpContext context, int id, CancellationToken cancellationToken)
        {
            try
            {

                await using var fileStream = await _mp3Service.GetAudioFileStream(id, AudioType.Mp3, false, cancellationToken);

                if (fileStream == null || fileStream == Stream.Null)
                {
                    _logger.LogWarning("File not found with id: {FileId}. GetMp4File returned null or Stream.Null.", id);
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync($"File with id '{id}' not found.", cancellationToken);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "audio/mpeg"; // "audio/mp4" or "audio/mpeg" for MP3
                // Use constants for header names for type safety and to avoid magic strings.
                context.Response.Headers[HeaderNames.CacheControl] = "no-cache"; // Or "public, max-age=xxxx" if caching is desired

                if (fileStream.CanSeek)
                {
                    context.Response.ContentLength = fileStream.Length;
                    // Ensure stream is at the beginning if its position was changed by checking Length.
                    // Most stream providers that give you Length will have Position at 0 if it's a fresh stream.
                    // fileStream.Position = 0;
                }
                await fileStream.CopyToAsync(context.Response.Body, cancellationToken);            }
            catch (FileNotFoundException ex) // Catch a specific exception if your service throws it
            {
                _logger.LogWarning(ex, "File not found exception for id: {FileId}", id);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync($"File with id '{id}' not found.", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming file with id: {FileId}", id);
                if (!context.Response.HasStarted) // Check if headers have already been sent
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    // Avoid sending detailed error information to the client in production
                    await context.Response.WriteAsync("An error occurred while streaming the file.", cancellationToken);
                }
                // If response has started, the connection will likely be aborted, or client receives partial content.
            }
        }

        /// <summary>
        /// Retrieves the last MP3 file for a specific language.
        /// </summary>
        public async Task<IActionResult> GetLast(HttpContext context, string language, CancellationToken cancellationToken)
        {
            try
            {
                var lastFile = await _mp3Service.GetLastMp3ByLanguageAsync(language, AudioType.Mp3, cancellationToken);
                if (lastFile == null)
                {
                    return NotFound();
                }

                return Ok(lastFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving last MP3 for language {Language}", language);
                return Problem("Failed to retrieve last MP3");
            }
        }
        /// <summary>
        /// Retrieves an MP3 file by its unique identifier (id) and returning it to the client
        /// </summary>
        public async Task<IActionResult> GetMp3FileById(HttpContext context, int id, CancellationToken cancellationToken)
        {
            try
            {
                var file = await _mp3Service.DownloadMp3(id, "tr", AudioType.Mp3, cancellationToken);
                if (file == null)
                {
                    return NotFound();
                }
                return Ok(file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving MP3 file with ID {Id}", id);
                return Problem("Failed to retrieve MP3 file");
            }
        }
        /// <summary>
        /// It is responsible for retrieving a list of MP3 files for a specific language.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IActionResult> GetMp3FileListByLanguage(HttpContext context, int id, CancellationToken cancellationToken)
        {
            try
            {
                var file = await _mp3Service.StreamMp3(id, AudioType.Mp3, cancellationToken);
                if (file == null)
                {
                    return NotFound();
                }
                return Ok(file);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving MP3 file with ID {Id}", id);
                return Problem("Failed to retrieve MP3 file");
            }
        }
        public async Task<IActionResult> DownloadFile(HttpContext context, int fileName, CancellationToken cancellationToken)
        {
            try
            {
                // Get file stream
                var fileBytes = await _mp3Service.GetMp3FileBytes(fileName, AudioType.Mp3, cancellationToken);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    return NotFound("File not found or is empty.");
                }
               // context.Response.ContentType = "audio/mp3"; // Set the content type for MP3 files
               // context.Response.ContentLength = fileBytes.Length;

                // Return streaming file response
                return File(fileBytes, "audio/mpeg", "callrecording.mp3", true);

            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Download operation cancelled for: {FileName}", fileName);
                return StatusCode(499, "Request cancelled"); // Non-standard code for client closed request
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming file: {FileName}", fileName);
                return StatusCode(500, "Could not download the requested file");
            }
        }

        internal async Task<IActionResult> SayText(HttpContext context, int id, CancellationToken token)
        {
            var request = new OneRequest
            {
                News = id,
                Language = "en-US",
            };
            try {
                await using var result = await _mp3Service.CreateSingleMp3Async(request, AudioType.Mp3, token);
                return new FileStreamResult(result, "audio/mpeg")
                {
                    FileDownloadName = $"{id}.mp3",
                    EnableRangeProcessing = true,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SayText Error processing MP3 request");
                return Problem("Failed to process MP3 request");
            }
        }

        internal async Task SayitText(HttpContext context, int id, CancellationToken token)
        {
             await _mp3Service.GetNewsList(token);
        }
        //public async Task Delete(string id)
        //{
        //    try
        //    {
        //        var file = await _mp3Service.GetMp3FileAsync(id);
        //        if (file == null)
        //        {
        //            await Results.NotFound().ExecuteAsync(HttpContext);
        //            return;
        //        }
        //        await _mp3Service.DeleteMp3FileAsync(file);
        //        await Results.Ok().ExecuteAsync(HttpContext);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error deleting MP3 file with ID {Id}", id);
        //        await Results.Problem("Failed to delete MP3 file").ExecuteAsync(HttpContext);
        //    }
    }
}