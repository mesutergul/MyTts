using Microsoft.Net.Http.Headers;

namespace MyTts.Services
{
    public class FileStreamingService : IFileStreamingService
    {
        private readonly ILogger<FileStreamingService> _logger;

        public FileStreamingService(ILogger<FileStreamingService> logger)
        {
            _logger = logger;
        }

        public async Task StreamAsync(
            HttpContext context,
            Stream fileStream,
            string fileName,
            string contentType = "application/octet-stream",
            CancellationToken cancellationToken = default)
        {
            if (fileStream == null || fileStream == Stream.Null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("File not found.", cancellationToken);
                _logger.LogWarning("File stream is null for: {FileName}", fileName);
                return;
            }

            try
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = contentType;
                context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                if (!string.IsNullOrWhiteSpace(fileName))
                    context.Response.Headers[HeaderNames.ContentDisposition] =
                        $"inline; filename=\"{fileName}\"";

                if (fileStream.CanSeek)
                {
                    fileStream.Position = 0;
                    context.Response.ContentLength = fileStream.Length;
                }

                await fileStream.CopyToAsync(context.Response.Body, 64 * 1024, cancellationToken);
                _logger.LogInformation("Successfully streamed file: {FileName}, Size: {FileSize} bytes", fileName, fileStream.Length);
            }
            catch (OperationCanceledException)
            {
                var phase = context.Response.HasStarted ? "after" : "before";
                _logger.LogDebug("Client cancelled {Phase} response for {FileName}", phase, fileName);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "File not found: {FileName}", fileName);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync("File not found.", cancellationToken);
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "IO error streaming: {FileName}", fileName);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync("IO error occurred.", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error streaming: {FileName}", fileName);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync("Unexpected error occurred.", cancellationToken);
                }
            }
            finally
            {
                await fileStream.DisposeAsync();
            }
        }
    }
}
