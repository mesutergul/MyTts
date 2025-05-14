using FFMpegCore;
using FFMpegCore.Pipes;
using MyTts.Repositories;

namespace MyTts.Services
{
    public sealed class Mp3StreamMerger : IMp3StreamMerger ,IAsyncDisposable
    {
        private readonly ILogger<Mp3StreamMerger> _logger;
        private readonly SemaphoreSlim _mergeLock;
        private bool _disposed;

        public Mp3StreamMerger(ILogger<Mp3StreamMerger> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mergeLock = new SemaphoreSlim(1, 1);
        }

        public async Task<(Stream audioData, string contentType, string fileName)> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audioProcessors);
            if (!audioProcessors.Any())
            {
                throw new ArgumentException("No audio processors provided", nameof(audioProcessors));
            }
            var outputFilePath = $"merged.{fileType.ToString().ToLower()}";
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
                    return await CreateSingleFileResultAsync(audioProcessors[0], outputFilePath, cancellationToken);
                }
                using var outputStream = new MemoryStream();
                await MergeAudioProcessorsAsync(audioProcessors, outputStream, outputFilePath, fileType, cancellationToken).ConfigureAwait(false);

                // Return the stream properly
                outputStream.Position = 0;
                _logger.LogInformation("Merged audio processors successfully");
                return (outputStream, "audio/mpeg", outputFilePath);
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
            CancellationToken cancellationToken)
        {
            // Create a list to track resources that need disposal
            var streamPipeSources = new List<StreamPipeSource>(processors.Count);
            var streamsToDispose = new List<Stream>(processors.Count);
            var codec = fileType.Equals(AudioType.Mp3) ? "libmp3lame" : "aac";
            try
            {
                var ffmpegArgs = await CreateFfmpegArgumentsAsync(processors, streamPipeSources, streamsToDispose, cancellationToken).ConfigureAwait(false);

                await ffmpegArgs
                    .OutputToPipe(
                        new StreamPipeSink(outputStream),
                        options => options
                            //.WithAudioCodec("copy")
                            .WithAudioCodec(codec)
                            .WithAudioBitrate(128)
                            .WithCustomArgument($"-f {fileType}")
                            .WithCustomArgument($"-filter_complex \"concat=n={processors.Count}:v=0:a=1[outa]\" -map \"[outa]\"")
                            .WithCustomArgument("-y"))
                    .CancellableThrough(cancellationToken)
                    .ProcessAsynchronously();
                // Ensure the outputStream contains data before trying to save
                if (outputStream.Length > 0)
                {
                    string fullPath = Path.Combine("audio", filePath);
                    _logger.LogInformation("Saving merged audio from MemoryStream to file: {FilePath}, length: {length}", filePath, outputStream.Length);

                    // Reset the MemoryStream position to the beginning so we can read from it
                    outputStream.Position = 0;

                    // Create a FileStream to write the MemoryStream content to disk
                    // FileMode.Create will create the file if it doesn't exist, or overwrite it if it does.
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
                    _logger.LogWarning("FFmpeg process completed but outputStream is empty. No data to save to file .");
                    // You might want to delete the potentially created empty file here if outputStream is empty
                    // try { if (File.Exists(outputFilePath)) File.Delete(outputFilePath); } catch { }
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
                // Ensure all resources are properly disposed
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

        private async Task<(Stream audioData, string contentType, string fileName)> CreateSingleFileResultAsync(AudioProcessor processor, string outputFilePath, CancellationToken cancellationToken)
        {
            var stream = await processor.GetStreamForCloudUploadAsync(cancellationToken);
            return (stream, "audio/mpeg", outputFilePath);
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
            var streams = new List<Stream>(processors.Count) { firstStream };

            // Process remaining streams
            for (int i = 1; i < processors.Count; i++)
            {
                var stream = await processors[i].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
                streams.Add(stream);
                streamsToDispose.Add(stream);
                var pipeSource = new StreamPipeSource(stream);
                streamPipeSources.Add(pipeSource);
                args.AddPipeInput(pipeSource);
            }

            // Create streams for each processor, getting them asynchronously
            // var streams = new List<Stream>(processors.Count);
            // for (int i = 0; i < processors.Count; i++)
            // {
            //     // Use the new async method for better memory efficiency
            //     var stream = await processors[i].GetStreamForCloudUploadAsync(cancellationToken).ConfigureAwait(false);
            //     streams.Add(stream);
            //     var pipeSource = new StreamPipeSource(stream);
            //     streamPipeSources.Add(pipeSource);
            // }

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