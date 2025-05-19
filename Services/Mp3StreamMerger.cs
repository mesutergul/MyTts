using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Configuration;
using MyTts.Models;
using MyTts.Repositories;
using MyTts.Services.Interfaces;

namespace MyTts.Services
{
    public sealed class Mp3StreamMerger : IMp3StreamMerger, IAsyncDisposable
    {
        private readonly ILogger<Mp3StreamMerger> _logger;
        private readonly SemaphoreSlim _mergeLock;
        private readonly string _baseStoragePath;
        private bool _disposed;

        public Mp3StreamMerger(ILogger<Mp3StreamMerger> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _baseStoragePath = Path.GetFullPath(configuration["Storage:BasePath"]) ?? "C:\\repos\\audio";
            _mergeLock = new SemaphoreSlim(1, 1);
        }

        public async Task<string> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            string? breakAudioPath = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audioProcessors);
            if (!audioProcessors.Any())
            {
                throw new ArgumentException("No audio processors provided", nameof(audioProcessors));
            }
            var outputFilePath = $"merged";
            var lockTaken = false;
            try
            {
                await _mergeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                lockTaken = true;
                
                if (audioProcessors.Count == 1)
                {
                    _logger.LogInformation("Only one processor provided - returning directly");
                    return outputFilePath;
                }
                
                using var outputStream = new MemoryStream();
                await MergeAudioProcessorsAsync(audioProcessors, outputStream, outputFilePath, fileType, breakAudioPath, cancellationToken).ConfigureAwait(false);

                outputStream.Position = 0;
                _logger.LogInformation("Merged audio processors successfully");
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
            finally
            {
                if (lockTaken)
                {
                    _mergeLock.Release();
                }
            }
        }

        private async Task MergeAudioProcessorsAsync(
            IReadOnlyList<AudioProcessor> processors,
            MemoryStream outputStream,
            string filePath,
            AudioType fileType,
            string? breakAudioPath,
            CancellationToken cancellationToken)
        {
            var streamPipeSources = new List<StreamPipeSource>();
            var streamsToDispose = new List<Stream>();
            var codec = fileType.Equals(AudioType.Mp3) ? "libmp3lame" : "aac";
            
            try
            {
                var ffmpegArgs = await CreateFfmpegArgumentsAsync(processors, streamPipeSources, streamsToDispose, breakAudioPath, cancellationToken).ConfigureAwait(false);
                
                // Calculate total number of inputs (processors + break audio between them)
                int totalInputs = breakAudioPath != null ? processors.Count * 2 - 1 : processors.Count;
                
                // Create the filter complex command for concatenating all inputs
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
                    string fullPath = Path.Combine(_baseStoragePath, filePath + "." + fileType.ToString().ToLower());
                    _logger.LogInformation("Saving merged audio from MemoryStream to file: {FilePath}, length: {length}", filePath, outputStream.Length);

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
                    _logger.LogInformation("Merged audio successfully saved to {FilePath}", filePath);
                }
                else
                {
                    _logger.LogWarning("FFmpeg process completed but outputStream is empty. No data to save to file.");
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
                _mergeLock?.Dispose();
                _disposed = true;
            }
            await ValueTask.CompletedTask;
        }
    }
}