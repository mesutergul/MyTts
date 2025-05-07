using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Pipes;

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

        public async Task<byte[]> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audioProcessors);
            if (!audioProcessors.Any())
            {
                throw new ArgumentException("No audio processors provided", nameof(audioProcessors));
            }

            await _mergeLock.WaitAsync(cancellationToken);
            try
            {
                using var outputStream = new MemoryStream();
                await MergeAudioProcessorsAsync(audioProcessors, outputStream, cancellationToken);
                return outputStream.ToArray();
            }
            finally
            {
                _mergeLock.Release();
            }
        }

        private async Task MergeAudioProcessorsAsync(
            IReadOnlyList<AudioProcessor> processors,
            MemoryStream outputStream,
            CancellationToken cancellationToken)
        {
            try
            {
                var ffmpegArgs = await CreateFfmpegArgumentsAsync(processors, cancellationToken);

                await ffmpegArgs
                    .OutputToPipe(
                        new StreamPipeSink(outputStream),
                        options => options
                            .WithAudioCodec("copy")
                            .WithCustomArgument($"-filter_complex \"concat=n={processors.Count}:v=0:a=1[outa]\" -map \"[outa]\"")
                            .WithCustomArgument("-y"))
                    .CancellableThrough(cancellationToken)
                    .ProcessAsynchronously();
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
        }

        private static async Task<FFMpegArguments> CreateFfmpegArgumentsAsync(
            IReadOnlyList<AudioProcessor> processors,
            CancellationToken cancellationToken)
        {           
            // Create FFmpeg arguments with all inputs  
            var args = FFMpegArguments.FromPipeInput(new StreamPipeSource(await processors[0].GetStreamForCloudUpload(cancellationToken)));
            for (int i = 1; i < processors.Count; i++)
            {
                args.AddPipeInput(new StreamPipeSource(await processors[i].GetStreamForCloudUpload(cancellationToken)));
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
