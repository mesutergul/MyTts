using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MyTts.Storage.Interfaces;
using MyTts.Storage.Models;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;

namespace MyTts.Storage
{
    public class LocalStorageClient : ILocalStorageClient, IAsyncDisposable
    {
        private readonly ILogger<LocalStorageClient> _logger;
        private readonly LocalStorageOptions _options;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
        private bool _disposed;

        public LocalStorageClient(
            ILogger<LocalStorageClient> logger,
            IOptions<LocalStorageOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _fileLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

            // Configure retry policy
            _retryPolicy = Policy
                .Handle<IOException>()
                .Or<UnauthorizedAccessException>()
                .WaitAndRetryAsync(
                    _options.MaxRetries,
                    retryAttempt => _options.RetryDelay * Math.Pow(2, retryAttempt - 1),
                    OnRetryAsync);

            // Configure circuit breaker policy
            _circuitBreakerPolicy = Policy
                .Handle<IOException>()
                .Or<UnauthorizedAccessException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 2,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (ex, duration) =>
                    {
                        _logger.LogWarning(ex, "Circuit breaker opened for {Duration} seconds", duration.TotalSeconds);
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Circuit breaker reset");
                    },
                    onHalfOpen: () =>
                    {
                        _logger.LogInformation("Circuit breaker half-open");
                    });
        }

        private Task OnRetryAsync(Exception ex, TimeSpan timeSpan, int retryCount, Context context)
        {
            _logger.LogWarning(ex, 
                "Retry {RetryCount} of {MaxRetries} after {Delay}ms for operation {Operation}",
                retryCount, _options.MaxRetries, timeSpan.TotalMilliseconds, context.OperationKey);
            return Task.CompletedTask;
        }

        private async Task<T> ExecuteWithPoliciesAsync<T>(Func<Task<T>> operation, string operationName)
        {
            return await _circuitBreakerPolicy
                .WrapAsync(_retryPolicy)
                .ExecuteAsync(async () => await operation());
        }

        public async Task<StorageResult<byte[]>> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var fileLock = await GetFileLockAsync(filePath);
                await fileLock.WaitAsync(cancellationToken);

                try
                {
                    var result = await ExecuteWithPoliciesAsync(
                        () => File.ReadAllBytesAsync(filePath, cancellationToken),
                        "ReadAllBytes");

                    return StorageResult<byte[]>.Success(result, DateTime.UtcNow - startTime);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogError(ex, "Circuit breaker is open for file: {FilePath}", filePath);
                return StorageResult<byte[]>.Failure(new StorageError(ex, "File system is temporarily unavailable"), DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read bytes from file: {FilePath}", filePath);
                return StorageResult<byte[]>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult<Stream>> ReadLargeFileAsStreamAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var stream = await ExecuteWithPoliciesAsync(
                    () => Task.FromResult(new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        _options.BufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan
                    )),
                    "ReadLargeFileAsStream");

                return StorageResult<Stream>.Success(stream, DateTime.UtcNow - startTime);
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogError(ex, "Circuit breaker is open for file: {FilePath}", filePath);
                return StorageResult<Stream>.Failure(new StorageError(ex, "File system is temporarily unavailable"), DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open file stream: {FilePath}", filePath);
                return StorageResult<Stream>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult> WriteAllBytesAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var fileLock = await GetFileLockAsync(filePath);
                await fileLock.WaitAsync(cancellationToken);

                try
                {
                    await ExecuteWithPoliciesAsync(async () =>
                    {
                        string? directory = Path.GetDirectoryName(filePath);
                        if (directory != null && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
                        return Task.CompletedTask;
                    }, "WriteAllBytes");

                    return StorageResult.Success(DateTime.UtcNow - startTime);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogError(ex, "Circuit breaker is open for file: {FilePath}", filePath);
                return StorageResult.Failure(new StorageError(ex, "File system is temporarily unavailable"), DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write bytes to file: {FilePath}", filePath);
                return StorageResult.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult> WriteAllTextAsync(string filePath, string contents, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var fileLock = await GetFileLockAsync(filePath);
                await fileLock.WaitAsync(cancellationToken);

                try
                {
                    await _retryPolicy.ExecuteAsync(async () =>
                        await File.WriteAllTextAsync(filePath, contents, cancellationToken));

                    return StorageResult.Success(DateTime.UtcNow - startTime);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write text to file: {FilePath}", filePath);
                return StorageResult.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult<string>> ReadAllTextAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var fileLock = await GetFileLockAsync(filePath);
                await fileLock.WaitAsync(cancellationToken);

                try
                {
                    var result = await _retryPolicy.ExecuteAsync(async () =>
                        await File.ReadAllTextAsync(filePath, cancellationToken));

                    return StorageResult<string>.Success(result, DateTime.UtcNow - startTime);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read text from file: {FilePath}", filePath);
                return StorageResult<string>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var fileLock = await GetFileLockAsync(filePath);
                await fileLock.WaitAsync(cancellationToken);

                try
                {
                    await _retryPolicy.ExecuteAsync(() =>
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                        return Task.CompletedTask;
                    });

                    return StorageResult.Success(DateTime.UtcNow - startTime);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file: {FilePath}", filePath);
                return StorageResult.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult<bool>> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    return File.Exists(filePath);
                });

                return StorageResult<bool>.Success(result, DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check file existence: {FilePath}", filePath);
                return StorageResult<bool>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult<bool>> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    return Directory.Exists(directoryPath);
                });

                return StorageResult<bool>.Success(result, DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check directory existence: {DirectoryPath}", directoryPath);
                return StorageResult<bool>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult> CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                await _retryPolicy.ExecuteAsync(() =>
                {
                    Directory.CreateDirectory(directoryPath);
                    return Task.CompletedTask;
                });

                return StorageResult.Success(DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create directory: {DirectoryPath}", directoryPath);
                return StorageResult.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult> MoveFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var sourceLock = await GetFileLockAsync(sourceFilePath);
                var destinationLock = await GetFileLockAsync(destinationFilePath);

                await Task.WhenAll(
                    sourceLock.WaitAsync(cancellationToken),
                    destinationLock.WaitAsync(cancellationToken)
                );

                try
                {
                    await _retryPolicy.ExecuteAsync(() =>
                    {
                        File.Move(sourceFilePath, destinationFilePath, true);
                        return Task.CompletedTask;
                    });

                    return StorageResult.Success(DateTime.UtcNow - startTime);
                }
                finally
                {
                    sourceLock.Release();
                    destinationLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move file from {SourcePath} to {DestinationPath}",
                    sourceFilePath, destinationFilePath);
                return StorageResult.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult<FileInfo>> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    return new FileInfo(filePath);
                });

                return StorageResult<FileInfo>.Success(result, DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get file info: {FilePath}", filePath);
                return StorageResult<FileInfo>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult<IEnumerable<string>>> ListFilesAsync(
            string directoryPath,
            string searchPattern = "*",
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    return Directory.GetFiles(directoryPath, searchPattern);
                });

                return StorageResult<IEnumerable<string>>.Success(result, DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list files in directory: {DirectoryPath}", directoryPath);
                return StorageResult<IEnumerable<string>>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult> SaveStreamAsync(Stream stream, string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var fileLock = await GetFileLockAsync(filePath);
                await fileLock.WaitAsync(cancellationToken);

                try
                {
                    await _retryPolicy.ExecuteAsync(async () =>
                    {
                        string? directory = Path.GetDirectoryName(filePath);
                        if (directory != null && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        await using var fileStream = new FileStream(
                            filePath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            _options.BufferSize,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);

                        await stream.CopyToAsync(fileStream, _options.BufferSize, cancellationToken);
                    });

                    return StorageResult.Success(DateTime.UtcNow - startTime);
                }
                finally
                {
                    fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save stream to file: {FilePath}", filePath);
                return StorageResult.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
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
                throw new ObjectDisposedException(nameof(LocalStorageClient));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                foreach (var fileLock in _fileLocks.Values)
                {
                    fileLock.Dispose();
                }
                _fileLocks.Clear();
                _disposed = true;
            }
            await ValueTask.CompletedTask;
        }
    }
} 