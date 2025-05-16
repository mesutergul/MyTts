namespace MyTts.Services.Interfaces
{
    public interface IFileStreamingService
    {
        Task StreamAsync(
            HttpContext context,
            Stream fileStream,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default);
    }
}
