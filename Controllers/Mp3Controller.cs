using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyTts.Models;
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
                await _mp3Service.CreateMultipleMp3Async(language, limit, cancellationToken);
                return Ok();
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
                var result = await _mp3Service.CreateSingleMp3Async(request, cancellationToken);
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
        public async Task<IActionResult> GetFile(HttpContext context, string id, CancellationToken cancellationToken)
        {
            try
            {
                if (!int.TryParse(id, out int parsedId))
                {
                    return BadRequest("Invalid ID format");
                }

                if (!await _mp3Service.FileExistsAnywhereAsync(id))
                {
                    _logger.LogWarning("File {FileName} not found", id);
                    return NotFound($"File {id} not found");
                }
                await using var fileStream = await _mp3Service.GetMp4File(id, cancellationToken); // Actually returns MP4
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "audio/mp4";
                context.Response.ContentLength = fileStream.Length;
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["Transfer-Encoding"] = "chunked";
                context.Response.Headers["Access-Control-Expose-Headers"] = "Content-Disposition";
                context.Response.Headers.Append("Access-Control-Allow-Origin", "http://127.0.0.1:5500");
                context.Response.Headers.Append("Access-Control-Allow-Headers", "*");
                context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, OPTIONS");
                context.Response.Headers.Append("Access-Control-Expose-Headers", "Content-Disposition");

                var buffer = new byte[65536];
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }

                return new EmptyResult(); // stream handled manually
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Download operation cancelled for: {FileName}", id);
                return StatusCode(499, "Request cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming file: {FileName}", id);
                return StatusCode(500, "Could not stream the requested file");
            }
        }
        /// <summary>
        /// Retrieves the last MP3 file for a specific language.
        /// </summary>
        public async Task<IActionResult> GetLast(HttpContext context, string language, CancellationToken cancellationToken)
        {
            try
            {
                var lastFile = await _mp3Service.GetLastMp3ByLanguageAsync(language, cancellationToken);
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
        public async Task<IActionResult> GetMp3FileById(HttpContext context, string id, CancellationToken cancellationToken)
        {
            try
            {
                var file = await _mp3Service.DownloadMp3(id, cancellationToken);
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
        public async Task<IActionResult> GetMp3FileListByLanguage(HttpContext context, string id, CancellationToken cancellationToken)
        {
            try
            {
                var file = await _mp3Service.StreamMp3(id, cancellationToken);
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
        public async Task<IActionResult> DownloadFile(string fileName, CancellationToken cancellationToken)
        {
            try
            {
                // Resolve full path (ensure proper path validation in production)
                string fullPath = Path.Combine("YourFilesDirectory", fileName);

                if (!System.IO.File.Exists(fullPath))
                {
                    return NotFound($"File {fileName} not found");
                }

                // Get file info for content type and length
                var fileInfo = new FileInfo(fullPath);

                // Get file stream
                var fileStream = await _mp3Service.GetMp4File(fullPath, cancellationToken);

                // Return streaming file response
                return new FileStreamResult(fileStream, "GetContentType(fileName)")
                {
                    FileDownloadName = fileName,
                    EnableRangeProcessing = true // Enables partial content requests
                };
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