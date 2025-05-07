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
            
            try
            {
                var ffmpegArgs = await CreateFfmpegArgumentsAsync(processors, streamPipeSources, cancellationToken).ConfigureAwait(false);

                await ffmpegArgs
                    .OutputToPipe(
                        new StreamPipeSink(outputStream),
                        options => options
                            .WithAudioCodec("copy")
                            .WithCustomArgument($"-filter_complex \"concat=n={processors.Count}:v=0:a=1[outa]\" -map \"[outa]\"")
                            .WithCustomArgument("-y"))
                    .CancellableThrough(cancellationToken)
                    .ProcessAsynchronously();
                
                return new FileContentResult(outputStream.ToArray(), "audio/mpeg");
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
            }
        }

        private static async Task<FFMpegArguments> CreateFfmpegArgumentsAsync(
            IReadOnlyList<AudioProcessor> processors,
            List<StreamPipeSource> streamPipeSources,
            CancellationToken cancellationToken)
        {
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

            // Create FFmpeg arguments with all inputs
            var args = FFMpegArguments.FromPipeInput(streamPipeSources[0]);
            for (int i = 1; i < streamPipeSources.Count; i++)
            {
                args.AddPipeInput(streamPipeSources[i]);
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