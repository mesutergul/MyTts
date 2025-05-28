using FFMpegCore;
using FFMpegCore.Pipes;
using MyTts.Models;
using MyTts.Services.Interfaces;
using MyTts.Helpers;
using System.Threading;

namespace MyTts.Services
{
    public sealed class Mp3StreamMerger : IMp3StreamMerger, IAsyncDisposable
    {
        private readonly ILogger<Mp3StreamMerger> _logger;
        private static readonly SemaphoreSlim _mergeSemaphore = new(MaxConcurrentMerges);
        private bool _disposed;
        private const int BufferSize = 81920; // 80KB buffer
        private const int MaxRetries = 3;
        private const int MaxConcurrentMerges = 2; // Limit concurrent merge operations
        private const int RetryDelayMs = 2000; // Base delay for retries
        private const int ProcessTimeoutMs = 30000; // 30 second timeout for FFmpeg process

        public Mp3StreamMerger(ILogger<Mp3StreamMerger> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            string breakAudioPath,
            string startAudioPath,
            string endAudioPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audioProcessors);
            if (!audioProcessors.Any())
            {
                throw new ArgumentException("No audio processors provided", nameof(audioProcessors));
            }
            var outputFilePath = "merged";

            try
            {
                if (audioProcessors.Count == 1)
                {
                    return outputFilePath;
                }

                await _mergeSemaphore.WaitAsync(cancellationToken);
                try
                {
                    using var outputStream = new MemoryStream();
                    await MergeAudioProcessorsAsync(audioProcessors, outputStream, outputFilePath, fileType, breakAudioPath, startAudioPath, endAudioPath, cancellationToken).ConfigureAwait(false);

                    outputStream.Position = 0;
                    return outputFilePath;
                }
                finally
                {
                    _mergeSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Merge operation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MergeMp3ByteArraysAsync");
                throw;
            }
        }

        private async Task MergeAudioProcessorsAsync(
            IReadOnlyList<AudioProcessor> processors,
            MemoryStream outputStream,
            string filePath,
            AudioType fileType,
            string breakAudioPath,
            string startAudioPath,
            string endAudioPath,
            CancellationToken cancellationToken)
        {
            var streamPipeSources = new List<StreamPipeSource>();
            var streamsToDispose = new List<Stream>();
            var codec = fileType.Equals(AudioType.Mp3) ? "libmp3lame" : "aac";
            var retryCount = 0;
            var lastException = default(Exception);

            try
            {
                while (retryCount < MaxRetries)
                {
                    try
                    {
                        // Check cancellation before starting the operation
                        cancellationToken.ThrowIfCancellationRequested();

                        var ffmpegArgs = await CreateFfmpegArgumentsAsync(processors, streamPipeSources, streamsToDispose, breakAudioPath, startAudioPath, endAudioPath, cancellationToken).ConfigureAwait(false);

                        int totalInputs = !string.IsNullOrEmpty(breakAudioPath) ? processors.Count * 2 - 2 : processors.Count;
                        totalInputs = !string.IsNullOrEmpty(startAudioPath) ? totalInputs + 1 : totalInputs;
                        totalInputs = !string.IsNullOrEmpty(endAudioPath) ? totalInputs + 1 : totalInputs;
                        var filterComplex = CreateFilterComplexCommand(totalInputs);

                        var ffmpegOptions = new FFOptions
                        {
                            BinaryFolder = Path.Combine(AppContext.BaseDirectory, "ffmpeg-bin"),
                            TemporaryFilesFolder = Path.GetTempPath()
                        };

                        // Create a linked cancellation token with timeout
                        using var timeoutCts = new CancellationTokenSource(ProcessTimeoutMs);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                        try
                        {
                            await ffmpegArgs
                                .OutputToPipe(
                                    new StreamPipeSink(outputStream),
                                    options => options
                                        .WithAudioCodec(codec)
                                        .WithAudioBitrate(128)
                                        .WithCustomArgument($"-f {fileType}")
                                        .WithCustomArgument(filterComplex)
                                        .WithCustomArgument("-y"))
                                .CancellableThrough(linkedCts.Token)
                                .ProcessAsynchronously(true, ffmpegOptions);

                            if (outputStream.Length > 0)
                            {
                                string fullPath = StoragePathHelper.GetFullPath(filePath, fileType);
                                _logger.LogInformation("Saving merged audio to {FilePath} ({Length} bytes)", filePath, outputStream.Length);

                                outputStream.Position = 0;
                                await using (var fileStream = new FileStream(
                                    fullPath,
                                    FileMode.Create,
                                    FileAccess.Write,
                                    FileShare.ReadWrite | FileShare.Delete,
                                    bufferSize: BufferSize,
                                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                                {
                                    try 
                                    {
                                        await outputStream.CopyToAsync(fileStream, BufferSize, linkedCts.Token).ConfigureAwait(false);
                                        await fileStream.FlushAsync(linkedCts.Token);
                                        return; // Success, exit the retry loop
                                    }
                                    catch (IOException ex)
                                    {
                                        _logger.LogError(ex, "Error writing to file {FilePath}", fullPath);
                                        throw new IOException($"Failed to write merged file: {ex.Message}", ex);
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogWarning("FFmpeg process completed but outputStream is empty");
                                throw new IOException("FFmpeg process completed but output stream is empty");
                            }
                        }
                        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                        {
                            _logger.LogWarning("FFmpeg process timed out after {Timeout}ms", ProcessTimeoutMs);
                            throw new TimeoutException($"FFmpeg process timed out after {ProcessTimeoutMs}ms");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Merge operation cancelled during attempt {RetryCount}", retryCount + 1);
                        throw;
                    }
                    catch (Exception ex) when (retryCount < MaxRetries - 1)
                    {
                        lastException = ex;
                        retryCount++;
                        var delay = RetryDelayMs * (int)Math.Pow(2, retryCount - 1); // Exponential backoff
                        _logger.LogWarning(ex, "Error during merge attempt {RetryCount}, retrying in {Delay}ms", retryCount, delay);
                        
                        try
                        {
                            await Task.Delay(delay, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation("Retry delay cancelled");
                            throw;
                        }

                        // Clean up resources before retry
                        foreach (var stream in streamsToDispose)
                        {
                            try { await stream.DisposeAsync(); } catch { }
                        }
                        streamsToDispose.Clear();
                        streamPipeSources.Clear();
                        outputStream.Position = 0;
                        outputStream.SetLength(0);
                    }
                }

                // If we get here, all retries failed
                throw new IOException($"Failed to merge audio files after {MaxRetries} attempts", lastException);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Merge operation cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error merging audio processors");
                throw;
            }
            finally
            {
                // Ensure all resources are cleaned up
                foreach (var source in streamPipeSources)
                {
                    try
                    {
                        ((IDisposable)source.Source).Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing StreamPipeSource");
                    }
                }
                foreach (var stream in streamsToDispose)
                {
                    try
                    {
                        await stream.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing Stream");
                    }
                }
            }
        }

        private string CreateFilterComplexCommand(int totalInputs)
        {
            // Create the filter complex command that concatenates all inputs
            var inputs = string.Join("][", Enumerable.Range(0, totalInputs));
            return $"-filter_complex \"[{inputs}]concat=n={totalInputs}:v=0:a=1[outa]\" -map \"[outa]\"";
        }

        private static async Task<FFMpegArguments> CreateFfmpegArgumentsAsync(
            IReadOnlyList<AudioProcessor> processors,
            List<StreamPipeSource> streamPipeSources,
            List<Stream> streamsToDispose,
            string? breakAudioPath,
            string? startAudioPath,
            string? endAudioPath,
            CancellationToken cancellationToken)
        {
            // Process the first stream
            var firstStream = await processors[0].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
            streamsToDispose.Add(firstStream);
            var firstPipeSource = new StreamPipeSource(firstStream);
            streamPipeSources.Add(firstPipeSource);

            // Create FFmpeg arguments with the first input
            var args = FFMpegArguments.FromPipeInput(firstPipeSource);
            if (!string.IsNullOrEmpty(startAudioPath))
            {
                args = args.AddFileInput(startAudioPath);
            }

            // Process remaining streams and add break audio between them if provided
            for (int i = 1; i < processors.Count; i++)
            {
                // Add the next processor's stream
                var stream = await processors[i].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
                streamsToDispose.Add(stream);
                var pipeSource = new StreamPipeSource(stream);
                streamPipeSources.Add(pipeSource);
                args = args.AddPipeInput(pipeSource);

                // Add break audio if provided
                if (!string.IsNullOrEmpty(breakAudioPath) && i < (processors.Count - 1))
                {
                    args = args.AddFileInput(breakAudioPath);
                }
            }

            if (!string.IsNullOrEmpty(endAudioPath))
            {
                args = args.AddFileInput(endAudioPath);
            }

            return args;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                // Don't dispose the static semaphore
            }
            await ValueTask.CompletedTask;
        }
    }
}