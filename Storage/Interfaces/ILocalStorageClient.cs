using MyTts.Storage.Models;

namespace MyTts.Storage.Interfaces
{
    public interface ILocalStorageClient
    {
        Task<StorageResult<byte[]>> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default);
        Task<StorageResult<Stream>> ReadLargeFileAsStreamAsync(string filePath, CancellationToken cancellationToken = default);
        Task<StorageResult> WriteAllBytesAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default);
        Task<StorageResult> WriteAllTextAsync(string filePath, string contents, CancellationToken cancellationToken = default);
        Task<StorageResult<string>> ReadAllTextAsync(string filePath, CancellationToken cancellationToken = default);
        Task<StorageResult> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);
        Task<StorageResult<bool>> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
        Task<StorageResult<bool>> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default);
        Task<StorageResult> CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
        Task<StorageResult> MoveFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default);
        Task<StorageResult<FileInfo>> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);
        Task<StorageResult<IEnumerable<string>>> ListFilesAsync(string directoryPath, string searchPattern = "*", CancellationToken cancellationToken = default);
        Task<StorageResult> SaveStreamAsync(Stream stream, string filePath, CancellationToken cancellationToken = default);
    }
} 