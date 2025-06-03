using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MyTts.Storage.Interfaces;
using MyTts.Storage.Models;
using MyTts.Config.ServiceConfigurations;
using MyTts.Helpers;
using Polly;

namespace MyTts.Storage
{
    public class LocalStorageClient : ILocalStorageClient, IAsyncDisposable
    {
        private readonly ILogger<LocalStorageClient> _logger;
        private readonly LocalStorageOptions _options;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks;
        private readonly SharedPolicyFactory _policyFactory;
        private readonly CombinedRateLimiter _rateLimiter;
        private bool _disposed;

        public LocalStorageClient(
            ILogger<LocalStorageClient> logger,
            IOptions<LocalStorageOptions> options,
            SharedPolicyFactory policyFactory,
            CombinedRateLimiter rateLimiter)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _policyFactory = policyFactory ?? throw new ArgumentNullException(nameof(policyFactory));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _fileLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        }

        private async Task<T> ExecuteWithPoliciesAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
        {
            var context = ResilienceContextPool.Shared.Get(cancellationToken);
            try
            {
                context.Properties.Set(new ResiliencePropertyKey<string>("OperationKey"), operationName);

                // Create a pipeline combining retry, circuit breaker, and rate limiting
                var pipeline = new ResiliencePipelineBuilder<T>()
                    .AddPipeline(_policyFactory.GetStorageRetryPolicy<T>())
                    .Build();

                // Acquire rate limit before executing the operation
                var linkedToken = _rateLimiter.CreateLinkedTokenWithTimeout(cancellationToken);
                await _rateLimiter.AcquireAsync(linkedToken);
                try
                {
                    return await pipeline.ExecuteAsync(async token => await operation(), context);
                }
                finally
                {
                    _rateLimiter.Release();
                }
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
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
                        $"ReadAllBytes_{filePath}",
                        cancellationToken);

                    return StorageResult<byte[]>.Success(result, DateTime.UtcNow - startTime);
                }
                finally
                {
                    fileLock.Release();
                }
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
                        FileShare.ReadWrite,
                        _options.BufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan
                    )),
                    $"ReadLargeFileAsStream_{filePath}",
                    cancellationToken);

                return StorageResult<Stream>.Success(stream, DateTime.UtcNow - startTime);
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
                    }, $"WriteAllBytes_{filePath}", cancellationToken);

                    return StorageResult.Success(DateTime.UtcNow - startTime);
                }
                finally
                {
                    fileLock.Release();
                }
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
                    await ExecuteWithPoliciesAsync<object>(
                        async () => 
                        {
                            await File.WriteAllTextAsync(filePath, contents, cancellationToken);
                            return null!;
                        },
                        $"WriteAllText_{filePath}",
                        cancellationToken);

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
                    var result = await ExecuteWithPoliciesAsync<string>(
                        async () => await File.ReadAllTextAsync(filePath, cancellationToken),
                        $"ReadAllText_{filePath}",
                        cancellationToken);

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
                    await ExecuteWithPoliciesAsync<object>(
                        () =>
                        {
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                            return Task.FromResult<object>(null!);
                        },
                        $"DeleteFile_{filePath}",
                        cancellationToken);

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
                var result = await ExecuteWithPoliciesAsync<bool>(
                    async () =>
                    {
                        await Task.CompletedTask;
                        return File.Exists(filePath);
                    },
                    $"FileExists_{filePath}",
                    cancellationToken);

                return StorageResult<bool>.Success(result, DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if file exists: {FilePath}", filePath);
                return StorageResult<bool>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult<bool>> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var result = await ExecuteWithPoliciesAsync<bool>(
                    async () =>
                    {
                        await Task.CompletedTask;
                        return Directory.Exists(directoryPath);
                    },
                    $"DirectoryExists_{directoryPath}",
                    cancellationToken);

                return StorageResult<bool>.Success(result, DateTime.UtcNow - startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if directory exists: {DirectoryPath}", directoryPath);
                return StorageResult<bool>.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult> CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                await ExecuteWithPoliciesAsync<object>(
                    () =>
                    {
                        Directory.CreateDirectory(directoryPath);
                        return Task.FromResult<object>(null!);
                    },
                    $"CreateDirectory_{directoryPath}",
                    cancellationToken);

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
                var destLock = await GetFileLockAsync(destinationFilePath);

                await sourceLock.WaitAsync(cancellationToken);
                await destLock.WaitAsync(cancellationToken);

                try
                {
                    await ExecuteWithPoliciesAsync<object>(
                        () =>
                        {
                            File.Move(sourceFilePath, destinationFilePath, true);
                            return Task.FromResult<object>(null!);
                        },
                        $"MoveFile_{sourceFilePath}_to_{destinationFilePath}",
                        cancellationToken);

                    return StorageResult.Success(DateTime.UtcNow - startTime);
                }
                finally
                {
                    sourceLock.Release();
                    destLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move file from {SourcePath} to {DestPath}", sourceFilePath, destinationFilePath);
                return StorageResult.Failure(new StorageError(ex), DateTime.UtcNow - startTime);
            }
        }

        public async Task<StorageResult<FileInfo>> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var result = await ExecuteWithPoliciesAsync<FileInfo>(
                    async () =>
                    {
                        await Task.CompletedTask;
                        return new FileInfo(filePath);
                    },
                    $"GetFileInfo_{filePath}",
                    cancellationToken);

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
                var result = await ExecuteWithPoliciesAsync<IEnumerable<string>>(
                    async () =>
                    {
                        await Task.CompletedTask;
                        return Directory.GetFiles(directoryPath, searchPattern);
                    },
                    $"ListFiles_{directoryPath}",
                    cancellationToken);

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
                    await ExecuteWithPoliciesAsync<object>(
                        async () =>
                        {
                            string? directory = Path.GetDirectoryName(filePath);
                            if (directory != null && !Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }

                            using var fileStream = new FileStream(
                                filePath,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.None,
                                _options.BufferSize,
                                FileOptions.Asynchronous | FileOptions.SequentialScan);

                            await stream.CopyToAsync(fileStream, _options.BufferSize, cancellationToken);
                            return null!;
                        },
                        $"SaveStream_{filePath}",
                        cancellationToken);

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