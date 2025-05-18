using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyTts.Services.Interfaces;

namespace MyTts.Services
{
    public class LocalStorageService : ILocalStorageService
    {
        private readonly ILogger<LocalStorageService> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks;
        private bool _disposed;

        public LocalStorageService(ILogger<LocalStorageService> logger)
        {
            _logger = logger;
            _fileLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        }

        public async Task SaveStreamToFileAsync(AudioProcessor processor, string localPath, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var fileLock = await GetFileLockAsync(localPath);
            await fileLock.WaitAsync();
            try
            {
                var fileOptions = FileOptions.Asynchronous | FileOptions.SequentialScan;

                await using (var fileStream = new FileStream(
                    localPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    fileOptions))
                {
                    await processor.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation("Saved file locally: {LocalPath}", localPath);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task<byte[]> ReadLargeFileAsync(string fullPath, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await using var fileStream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            try
            {
                var totalBytes = new byte[fileStream.Length];
                var bytesRead = 0;
                var buffer = new byte[128 * 1024];

                while (bytesRead < totalBytes.Length)
                {
                    var count = await fileStream.ReadAsync(
                        buffer.AsMemory(0, Math.Min(128 * 1024, totalBytes.Length - bytesRead)),
                        cancellationToken
                    );

                    if (count == 0) break; // End of stream

                    Buffer.BlockCopy(buffer, 0, totalBytes, bytesRead, count);
                    bytesRead += count;
                }

                return totalBytes;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("File read operation cancelled: {Path}", fullPath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading large file: {Path}", fullPath);
                throw new IOException($"Failed to read large file: {fullPath}", ex);
            }
        }

        public Task<Stream> ReadLargeFileAsStreamAsync(string fullPath, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            try
            {
                var fileStream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                );

                fileStream.Position = 0;
                return Task.FromResult<Stream>(fileStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error opening file stream: {Path}", fullPath);
                throw new IOException($"Failed to open file stream: {fullPath}", ex);
            }
        }

        public async Task DeleteFileAsync(string path)
        {
            ThrowIfDisposed();
            var lockForFile = await GetFileLockAsync(path);
            await lockForFile.WaitAsync();
            try
            {
                if (await FileExistsAsync(path))
                {
                    File.Delete(path);
                }
            }
            finally
            {
                lockForFile.Release();
            }
        }

        public async Task<IEnumerable<string>> ListFilesAsync(string directoryPath, string searchPattern = "*")
        {
            ThrowIfDisposed();
            return await Task.Run(() => Directory.GetFiles(directoryPath, searchPattern).AsEnumerable());
        }

        public Task<bool> FileExistsAsync(string filePath)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));
            }

            return Task.Run(() => File.Exists(filePath));
        }

        public async Task<bool> DirectoryExistsAsync(string directoryPath)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("Directory path cannot be null or whitespace.", nameof(directoryPath));
            }

            return await Task.Run(() => Directory.Exists(directoryPath));
        }

        public async Task CreateDirectoryAsync(string directoryPath)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("Directory path cannot be null or whitespace.", nameof(directoryPath));
            }

            try
            {
                Directory.CreateDirectory(directoryPath);
                _logger.LogInformation("Created directory: {DirectoryPath}", directoryPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory: {DirectoryPath}", directoryPath);
                throw;
            }
        }

        public async Task WriteAllBytesAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var fileLock = await GetFileLockAsync(filePath);
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
                _logger.LogInformation("Wrote {ByteCount} bytes to file: {FilePath}", bytes.Length, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write bytes to file: {FilePath}", filePath);
                throw;
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task<string> ReadAllTextAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var fileLock = await GetFileLockAsync(filePath);
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                return await File.ReadAllTextAsync(filePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read text from file: {FilePath}", filePath);
                throw;
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task WriteAllTextAsync(string filePath, string contents, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var fileLock = await GetFileLockAsync(filePath);
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                await File.WriteAllTextAsync(filePath, contents, cancellationToken);
                _logger.LogInformation("Wrote text content to file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write text to file: {FilePath}", filePath);
                throw;
            }
            finally
            {
                fileLock.Release();
            }
        }

        public async Task MoveFileAsync(string sourceFilePath, string destinationFilePath)
        {
            ThrowIfDisposed();
            var sourceLock = await GetFileLockAsync(sourceFilePath);
            var destinationLock = await GetFileLockAsync(destinationFilePath);
            
            await Task.WhenAll(
                sourceLock.WaitAsync(),
                destinationLock.WaitAsync()
            );
            
            try
            {
                File.Move(sourceFilePath, destinationFilePath, true);
                _logger.LogInformation("Moved file from {SourcePath} to {DestinationPath}", 
                    sourceFilePath, destinationFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move file from {SourcePath} to {DestinationPath}", 
                    sourceFilePath, destinationFilePath);
                throw;
            }
            finally
            {
                sourceLock.Release();
                destinationLock.Release();
            }
        }

        public Task<FileInfo> GetFileInfoAsync(string filePath)
        {
            ThrowIfDisposed();
            try
            {
                return Task.FromResult(new FileInfo(filePath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get file info: {FilePath}", filePath);
                throw;
            }
        }

        public async Task<byte[]> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var fileLock = await GetFileLockAsync(filePath);
            await fileLock.WaitAsync(cancellationToken);
            try
            {
                return await File.ReadAllBytesAsync(filePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read bytes from file: {FilePath}", filePath);
                throw;
            }
            finally
            {
                fileLock.Release();
            }
        }

        private Task<SemaphoreSlim> GetFileLockAsync(string filePath)
        {
            ThrowIfDisposed();
            return Task.FromResult(_fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1)));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LocalStorageService));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await DisposeAsyncCore().ConfigureAwait(false);
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            // Dispose of all file locks
            foreach (var fileLock in _fileLocks.Values)
            {
                try
                {
                    fileLock.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing file lock");
                }
            }
            _fileLocks.Clear();

            await Task.CompletedTask; // For potential future async cleanup
        }
    }
}
