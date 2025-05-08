using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MyTts.Services
{
    public sealed class Mp3StreamMerger : IAsyncDisposable
    {
        private readonly ILogger<Mp3StreamMerger> _logger;
        private readonly SemaphoreSlim _mergeLock;
        private bool _disposed;

        public Mp3StreamMerger(ILogger<Mp3StreamMerger> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mergeLock = new SemaphoreSlim(1, 1);
        }

        public async Task<IActionResult> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audioProcessors);
            if (!audioProcessors.Any())
            {
                throw new ArgumentException("No audio processors provided", nameof(audioProcessors));
            }

            // Use ValueTask-based pattern for better async efficiency
            var lockTaken = false;
            try
            {
                await _mergeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                lockTaken = true;
                // For a single audio processor, return directly without merging
                if (audioProcessors.Count == 1)
                {
                    _logger.LogInformation("Only one processor provided - returning directly");
                    return await CreateSingleFileResultAsync(audioProcessors[0], cancellationToken);
                }
                using var outputStream = new MemoryStream();
                await MergeAudioProcessorsAsync(audioProcessors, outputStream, cancellationToken).ConfigureAwait(false);
                
                // Return the stream properly
                outputStream.Position = 0;
                return new FileStreamResult(outputStream, "audio/mpeg") 
                {
                    FileDownloadName = $"merged_{DateTime.UtcNow:yyyyMMddHHmmss}.mp3"
                };
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
            finally
            {
                if (lockTaken)
                {
                    _mergeLock.Release();
                }
            }
        }

        private async Task<IActionResult> MergeAudioProcessorsAsync(
            IReadOnlyList<AudioProcessor> processors,
            MemoryStream outputStream,
            CancellationToken cancellationToken)
        {
            // Create a list to track resources that need disposal
            var streamPipeSources = new List<StreamPipeSource>(processors.Count);
            var streamsToDispose = new List<Stream>(processors.Count);
            try
            {
                var ffmpegArgs = await CreateFfmpegArgumentsAsync(processors, streamPipeSources, streamsToDispose, cancellationToken).ConfigureAwait(false);

                await ffmpegArgs
                    .OutputToPipe(
                        new StreamPipeSink(outputStream),
                        options => options
                            .WithAudioCodec("copy")
                            .WithCustomArgument($"-filter_complex \"concat=n={processors.Count}:v=0:a=1[outa]\" -map \"[outa]\"")
                            .WithCustomArgument("-y"))
                    .CancellableThrough(cancellationToken)
                    .ProcessAsynchronously();
                
                return new FileStreamResult(outputStream, "audio/mpeg")
                {
                    EnableRangeProcessing = true,
                    FileDownloadName = $"merged_{DateTime.UtcNow:yyyyMMddHHmmss}.mp3"
                };
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
                // Ensure all resources are properly disposed
                foreach (var source in streamPipeSources)
                {
                    try
                    {
                        ((IDisposable)source).Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing StreamPipeSource");
                    }
                }
                // Dispose the streams we created
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
        private async Task<IActionResult> CreateSingleFileResultAsync(AudioProcessor processor, CancellationToken cancellationToken)
        {
            var stream = await processor.GetStreamForCloudUploadAsync(cancellationToken);
            return new FileStreamResult(stream, "audio/mpeg")
            {
                FileDownloadName = $"audio_{DateTime.UtcNow:yyyyMMddHHmmss}.mp3",
                EnableRangeProcessing = true
            };
        }
        private static async Task<FFMpegArguments> CreateFfmpegArgumentsAsync(
            IReadOnlyList<AudioProcessor> processors,
            List<StreamPipeSource> streamPipeSources,
            List<Stream> streamsToDispose,
            CancellationToken cancellationToken)
        {
            // Process the first stream
            var firstStream = await processors[0].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
            streamsToDispose.Add(firstStream);
            var firstPipeSource = new StreamPipeSource(firstStream);
            streamPipeSources.Add(firstPipeSource);

            // Create FFmpeg arguments with the first input
            var args = FFMpegArguments.FromPipeInput(firstPipeSource);

            // Process remaining streams
            for (int i = 1; i < processors.Count; i++)
            {
                var stream = await processors[i].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
                streamsToDispose.Add(stream);
                var pipeSource = new StreamPipeSource(stream);
                streamPipeSources.Add(pipeSource);
                args.AddPipeInput(pipeSource);
            }

            // Create streams for each processor, getting them asynchronously
            var streams = new List<Stream>(processors.Count);
            for (int i = 0; i < processors.Count; i++)
            {
                // Use the new async method for better memory efficiency
                var stream = await processors[i].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
                streams.Add(stream);
                var pipeSource = new StreamPipeSource(stream);
                streamPipeSources.Add(pipeSource);
            }

            return args;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _mergeLock?.Dispose();
                _disposed = true;
            }
            await ValueTask.CompletedTask;
        }
    }
}