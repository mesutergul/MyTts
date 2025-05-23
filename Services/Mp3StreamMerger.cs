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
        private const int BufferSize = 81920; // 80KB buffer
        private const int MaxRetries = 3;

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
                
                using var outputStream = new MemoryStream();
                await MergeAudioProcessorsAsync(audioProcessors, outputStream, outputFilePath, fileType, breakAudioPath, startAudioPath, endAudioPath, cancellationToken).ConfigureAwait(false);

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
            string startAudioPath,
            string endAudioPath,
            CancellationToken cancellationToken)
        {
            var streamPipeSources = new List<StreamPipeSource>();
            var streamsToDispose = new List<Stream>();
            var codec = fileType.Equals(AudioType.Mp3) ? "libmp3lame" : "aac";
            var retryCount = 0;
            
            try
            {
                while (retryCount < MaxRetries)
                {
                    try
                    {
                        var ffmpegArgs = await CreateFfmpegArgumentsAsync(processors, streamPipeSources, streamsToDispose, breakAudioPath, startAudioPath, endAudioPath, cancellationToken).ConfigureAwait(false);
                        
                        int totalInputs = !string.IsNullOrEmpty(breakAudioPath) ? processors.Count * 2 - 2 : processors.Count;
                        totalInputs = !string.IsNullOrEmpty(startAudioPath) ? totalInputs + 1 : totalInputs;
                        totalInputs = !string.IsNullOrEmpty(endAudioPath) ? totalInputs + 1 : totalInputs;
                        var filterComplex = CreateFilterComplexCommand(totalInputs);

                        // Configure FFmpeg with proper settings
                        var ffmpegOptions = new FFOptions
                        {
                            BinaryFolder = Path.Combine(AppContext.BaseDirectory, "ffmpeg-bin"),
                            TemporaryFilesFolder = Path.GetTempPath()
                        };

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
                                bufferSize: BufferSize,
                                FileOptions.Asynchronous | FileOptions.SequentialScan))
                            {
                                await outputStream.CopyToAsync(fileStream, BufferSize, cancellationToken).ConfigureAwait(false);
                            }
                            return; // Success, exit the retry loop
                        }
                        else
                        {
                            _logger.LogWarning("FFmpeg process completed but outputStream is empty");
                            throw new IOException("FFmpeg process completed but output stream is empty");
                        }
                    }
                    catch (IOException ex) when (ex.Message.Contains("Pipe is broken") && retryCount < MaxRetries - 1)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "Pipe error during merge, retry {RetryCount} of {MaxRetries}", retryCount, MaxRetries);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), cancellationToken); // Exponential backoff
                        outputStream.Position = 0; // Reset stream position
                        
                        // Clean up streams before retry
                        foreach (var stream in streamsToDispose)
                        {
                            try { await stream.DisposeAsync(); } catch { }
                        }
                        streamsToDispose.Clear();
                        streamPipeSources.Clear();
                    }
                }
                throw new IOException($"Failed to merge audio files after {MaxRetries} attempts");
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
            }
            await ValueTask.CompletedTask;
        }
    }
}