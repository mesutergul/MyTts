using FFMpegCore;
using FFMpegCore.Pipes;
using MyTts.Models;
using MyTts.Services.Interfaces;
using MyTts.Helpers;

namespace MyTts.Services
{
    public sealed class Mp3StreamMerger : IMp3StreamMerger, IAsyncDisposable
    {
        private readonly ILogger<Mp3StreamMerger> _logger;
        private bool _disposed;

        public Mp3StreamMerger(ILogger<Mp3StreamMerger> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            string breakAudioPath,
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
                
                using var outputStream = new MemoryStream();
                await MergeAudioProcessorsAsync(audioProcessors, outputStream, outputFilePath, fileType, breakAudioPath, cancellationToken).ConfigureAwait(false);

                outputStream.Position = 0;
                return outputFilePath;
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
            CancellationToken cancellationToken)
        {
            var streamPipeSources = new List<StreamPipeSource>();
            var streamsToDispose = new List<Stream>();
            var codec = fileType.Equals(AudioType.Mp3) ? "libmp3lame" : "aac";
            
            try
            {
                var ffmpegArgs = await CreateFfmpegArgumentsAsync(processors, streamPipeSources, streamsToDispose, breakAudioPath, cancellationToken).ConfigureAwait(false);
                
                int totalInputs = !string.IsNullOrEmpty(breakAudioPath) ? processors.Count * 2 - 1 : processors.Count;
                var filterComplex = CreateFilterComplexCommand(totalInputs);

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
                    .ProcessAsynchronously();

                if (outputStream.Length > 0)
                {
                    string fullPath = StoragePathHelper.GetFullPath(filePath, fileType);
                    _logger.LogInformation("Saving merged audio to {FilePath} ({Length} bytes)", filePath, outputStream.Length);

                    outputStream.Position = 0;
                    await using (var fileStream = new FileStream(
                        fullPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 128*1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await outputStream.CopyToAsync(fileStream, 128*1024, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    _logger.LogWarning("FFmpeg process completed but outputStream is empty");
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

        private async Task<(Stream audioData, string contentType, string fileName)> CreateSingleFileResultAsync(
            AudioProcessor processor, string outputFilePath, CancellationToken cancellationToken)
        {
            var stream = await processor.GetStreamForCloudUploadAsync(cancellationToken);
            return (stream, "audio/mpeg", outputFilePath);
        }

        private static async Task<FFMpegArguments> CreateFfmpegArgumentsAsync(
            IReadOnlyList<AudioProcessor> processors,
            List<StreamPipeSource> streamPipeSources,
            List<Stream> streamsToDispose,
            string? breakAudioPath,
            CancellationToken cancellationToken)
        {
            // Process the first stream
            var firstStream = await processors[0].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
            streamsToDispose.Add(firstStream);
            var firstPipeSource = new StreamPipeSource(firstStream);
            streamPipeSources.Add(firstPipeSource);

            // Create FFmpeg arguments with the first input
            var args = FFMpegArguments.FromPipeInput(firstPipeSource);

            // Process remaining streams and add break audio between them if provided
            for (int i = 1; i < processors.Count; i++)
            {
                // Add break audio if provided
                if (breakAudioPath != null)
                {
                    args.AddFileInput(breakAudioPath);
                }

                // Add the next processor's stream
                var stream = await processors[i].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
                streamsToDispose.Add(stream);
                var pipeSource = new StreamPipeSource(stream);
                streamPipeSources.Add(pipeSource);
                args.AddPipeInput(pipeSource);
            }

            return args;
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