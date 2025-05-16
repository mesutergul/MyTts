using Microsoft.Net.Http.Headers;

namespace MyTts.Helpers
{
    public static class FileStreamingHelper
    {
        public static async Task StreamFileAsync(
            HttpContext context,
            Stream fileStream,
            string fileName,
            string contentType = "application/octet-stream",
            ILogger logger = null,
            CancellationToken cancellationToken = default)
        {
            if (fileStream == null || fileStream == Stream.Null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("File not found.", cancellationToken);
                logger?.LogWarning("File stream is null or Stream.Null for file: {FileName}", fileName);
                return;
            }

            try
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    context.Response.Headers[HeaderNames.ContentDisposition] =
                        $"inline; filename=\"{fileName}\"";
                }

                context.Response.Headers[HeaderNames.CacheControl] = "no-cache";

                if (fileStream.CanSeek)
                {
                    fileStream.Position = 0;
                    context.Response.ContentLength = fileStream.Length;
                }

                await fileStream.CopyToAsync(context.Response.Body, 64 * 1024, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (context.Response.HasStarted)
                    logger?.LogDebug("Client cancelled the request after response started for file: {FileName}", fileName);
                else
                    logger?.LogWarning("Client cancelled the request before response started for file: {FileName}", fileName);
                // No further response writing
            }
            catch (FileNotFoundException ex)
            {
                logger?.LogWarning(ex, "File not found: {FileName}", fileName);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync("File not found.", cancellationToken);
                }
            }
            catch (IOException ex)
            {
                logger?.LogError(ex, "IO error while streaming file: {FileName}", fileName);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync("IO error occurred.", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error while streaming file: {FileName}", fileName);
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync("Unexpected error occurred.", cancellationToken);
                }
            }
            finally
            {
                await fileStream.DisposeAsync(); // dispose edilen stream sorumluluğu buraya taşındı
            }
        }
    }
}
