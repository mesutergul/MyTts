using FFMpegCore;
using FFMpegCore.Pipes;
using MyTts.Models;
using MyTts.Services.Interfaces;
using MyTts.Helpers;
using Polly;
using MyTts.Config.ServiceConfigurations;

namespace MyTts.Services
{
    public sealed class Mp3StreamMerger : IMp3StreamMerger, IAsyncDisposable
    {
        private readonly ILogger<Mp3StreamMerger> _logger;
        private readonly CombinedRateLimiter _rateLimiter;
        private readonly ResiliencePipeline<string> _mergePipeline;
        private bool _disposed;
        private const int BufferSize = 81920; // 80KB buffer
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 2000; // Base delay for retries
        private const int ProcessTimeoutMs = 30000; // 30 second timeout for FFmpeg process
        private static readonly TimeSpan RateLimitTimeout = TimeSpan.FromSeconds(30);
        private static readonly ResiliencePropertyKey<string> OperationKey = new("OperationKey");

        public Mp3StreamMerger(
            ILogger<Mp3StreamMerger> logger, 
            IConfiguration configuration,
            CombinedRateLimiter rateLimiter,
            SharedPolicyFactory policyFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _mergePipeline = policyFactory.CreatePipeline<string>(MaxRetries, RetryDelayMs / 1000);
        }

        public async Task<string> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            string breakAudioPath,
            string startAudioPath,
            string endAudioPath,
            ResilienceContext resilienceContext,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audioProcessors);
            ArgumentNullException.ThrowIfNull(resilienceContext);
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

                // Create a linked token with timeout for rate limiting
                var linkedToken = _rateLimiter.CreateLinkedTokenWithTimeout(cancellationToken, RateLimitTimeout);

                // Acquire rate limit before starting the merge operation
                await _rateLimiter.AcquireAsync(linkedToken);
                try
                {
                    return await MergeAudioProcessorsAsync(
                        audioProcessors, 
                        outputFilePath, 
                        fileType, 
                        breakAudioPath, 
                        startAudioPath, 
                        endAudioPath, 
                        linkedToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _rateLimiter.Release();
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

        private async Task<string> MergeAudioProcessorsAsync(
            IReadOnlyList<AudioProcessor> processors,
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

            try
            {
                var context = ResilienceContextPool.Shared.Get(cancellationToken);
                try
                {
                    context.Properties.Set(OperationKey, "MergeAudioProcessors");
                    
                    return await _mergePipeline.ExecuteAsync(async (ctx) =>
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var ffmpegArgs = await CreateFfmpegArgumentsAsync(
                                processors, 
                                streamPipeSources, 
                                streamsToDispose, 
                                breakAudioPath, 
                                startAudioPath, 
                                endAudioPath, 
                                cancellationToken)
                                .ConfigureAwait(false);

                            int totalInputs = CalculateTotalInputs(processors.Count, breakAudioPath, startAudioPath, endAudioPath);
                            var filterComplex = CreateFilterComplexCommand(totalInputs);

                            var ffmpegOptions = new FFOptions
                            {
                                BinaryFolder = Path.Combine(AppContext.BaseDirectory, "ffmpeg-bin"),
                                TemporaryFilesFolder = Path.GetTempPath()
                            };

                            using var outputStream = new MemoryStream();
                            await ffmpegArgs
                                .OutputToPipe(
                                    new StreamPipeSink(outputStream),
                                    options => options
                                        .WithAudioCodec(codec)
                                        .WithAudioBitrate(128)
                                        .WithCustomArgument($"-f {fileType}")
                                        .WithCustomArgument(filterComplex)
                                        .WithCustomArgument("-y"))
                                .CancellableThrough(cancellationToken)
                                .ProcessAsynchronously(true, ffmpegOptions);

                            if (outputStream.Length == 0)
                            {
                                throw new InvalidOperationException("FFmpeg process completed but output stream is empty");
                            }

                            string fullPath = StoragePathHelper.GetFullPath(filePath, fileType);
                            _logger.LogInformation("Saving merged audio to {FilePath} ({Length} bytes)", filePath, outputStream.Length);

                            outputStream.Position = 0;
                            await using var fileStream = new FileStream(
                                fullPath,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.ReadWrite | FileShare.Delete,
                                bufferSize: BufferSize,
                                FileOptions.Asynchronous | FileOptions.SequentialScan);

                            await outputStream.CopyToAsync(fileStream, BufferSize, cancellationToken);
                            return fullPath;
                        }
                        catch (Exception)
                        {
                            await CleanupStreamsAsync(streamPipeSources, streamsToDispose);
                            throw;
                        }
                    }, context);
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(context);
                }
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
                await CleanupStreamsAsync(streamPipeSources, streamsToDispose);
            }
        }

        private static int CalculateTotalInputs(int processorCount, string? breakAudioPath, string? startAudioPath, string? endAudioPath)
        {
            int totalInputs = processorCount;
            
            if (!string.IsNullOrEmpty(breakAudioPath))
            {
                totalInputs += processorCount - 2; // Add break audio between each processor
            }
            
            if (!string.IsNullOrEmpty(startAudioPath))
            {
                totalInputs++;
            }
            
            if (!string.IsNullOrEmpty(endAudioPath))
            {
                totalInputs++;
            }
            
            return totalInputs;
        }

        private async Task CleanupStreamsAsync(List<StreamPipeSource> streamPipeSources, List<Stream> streamsToDispose)
        {
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
            streamPipeSources.Clear();

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
            streamsToDispose.Clear();
        }

        private static string CreateFilterComplexCommand(int totalInputs)
        {
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

        // Custom exception for FFmpeg errors
        public class FFmpegException : Exception
        {
            public FFmpegException(string message) : base(message) { }
            public FFmpegException(string message, Exception innerException) : base(message, innerException) { }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
            await ValueTask.CompletedTask;
        }
    }
}