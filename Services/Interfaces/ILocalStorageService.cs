namespace MyTts.Services.Interfaces
{
    public interface ILocalStorageService : IAsyncDisposable
    {
        Task SaveStreamToFileAsync(AudioProcessor processor, string localPath, CancellationToken cancellationToken);
        Task<Stream> ReadLargeFileAsStreamAsync(string fullPath, CancellationToken cancellationToken);
        Task<byte[]> ReadLargeFileAsync(string fullPath, CancellationToken cancellationToken);
        Task DeleteFileAsync(string filePath);
        Task<IEnumerable<string>> ListFilesAsync(string directoryPath, string searchPattern = "*");
        Task<bool> FileExistsAsync(string filePath);
        Task<bool> DirectoryExistsAsync(string directoryPath);
    }
}