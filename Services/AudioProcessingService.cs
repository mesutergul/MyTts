using Microsoft.Extensions.Options;
using MyTts.Models;
using MyTts.Services.Interfaces;
using MyTts.Storage;
using FFMpegCore;

namespace MyTts.Services
{
    public class AudioProcessingService : IAudioProcessingService, IDisposable
    {
        private readonly ILogger<AudioProcessingService> _logger;
        private readonly StorageConfiguration _storageConfig;
        private readonly SemaphoreSlim _processingSemaphore;
        private const int MaxConcurrentProcessing = 3;
        private const int DefaultBufferSize = 81920; // 80KB buffer
        private bool _disposed;

        public AudioProcessingService(
            ILogger<AudioProcessingService> logger,
            IOptions<StorageConfiguration> storageConfig)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageConfig = storageConfig?.Value ?? throw new ArgumentNullException(nameof(storageConfig));
            _processingSemaphore = new SemaphoreSlim(MaxConcurrentProcessing);
        }

        public async Task<byte[]> ConvertToMp3Async(byte[] audioData, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _logger.LogInformation("Converting audio to MP3 format");
            return await ProcessAudioAsync(audioData, options => options
                .WithAudioCodec("libmp3lame")
                .WithAudioBitrate(128)
                .WithCustomArgument("-f mp3")
                .WithCustomArgument("-map_metadata 0"), // Preserve metadata
                cancellationToken);
        }

        public async Task<byte[]> ConvertToM4aAsync(byte[] audioData, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _logger.LogInformation("Converting audio to M4A format");
            return await ProcessAudioAsync(audioData, options => options
                .WithAudioCodec("aac")
                .WithAudioBitrate(128)
                .WithCustomArgument("-f m4a")
                .WithCustomArgument("-map_metadata 0"), // Preserve metadata
                cancellationToken);
        }

        public async Task<byte[]> ConvertToWavAsync(byte[] audioData, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _logger.LogInformation("Converting audio to WAV format");
            return await ProcessAudioAsync(audioData, options => options
                .WithAudioCodec("pcm_s16le")
                .WithCustomArgument("-f wav")
                .WithCustomArgument("-map_metadata 0"), // Preserve metadata
                cancellationToken);
        }

        public async Task<AudioMetadata> ExtractMetadataAsync(byte[] audioData, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _logger.LogInformation("Extracting audio metadata");
            
            var tempFile = Path.Combine(Path.GetTempPath(), $"metadata_{Guid.NewGuid()}.tmp");
            try
            {
                await File.WriteAllBytesAsync(tempFile, audioData, cancellationToken);
                var mediaInfo = await FFProbe.AnalyseAsync(tempFile, cancellationToken: cancellationToken);
                
                if (mediaInfo == null)
                {
                    throw new InvalidOperationException("Failed to analyze audio file");
                }

                var tags = mediaInfo.Format.Tags ?? new Dictionary<string, string>();
                var audioStream = mediaInfo.PrimaryAudioStream;

                if (audioStream == null)
                {
                    throw new InvalidOperationException("No audio stream found in the file");
                }

                return new AudioMetadata
                {
                    Title = tags.GetValueOrDefault("title", string.Empty),
                    Artist = tags.GetValueOrDefault("artist", string.Empty),
                    Album = tags.GetValueOrDefault("album", string.Empty),
                    Year = int.TryParse(tags.GetValueOrDefault("date", "0"), out var year) ? year : 0,
                    Language = tags.GetValueOrDefault("language", string.Empty),
                    Duration = TimeSpan.FromSeconds(mediaInfo.Duration.TotalSeconds),
                    BitRate = (int)mediaInfo.Format.BitRate,
                    SampleRate = audioStream.SampleRateHz,
                    Channels = audioStream.Channels,
                    Format = mediaInfo.Format.FormatName,
                    Codec = audioStream.CodecName
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting metadata from audio file");
                throw;
            }
            finally
            {
                SafeDeleteFile(tempFile);
            }
        }

        public async Task<byte[]> UpdateMetadataAsync(byte[] audioData, AudioMetadata metadata, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _logger.LogInformation("Updating audio metadata");

            var metadataArgs = new List<string>
            {
                $"-metadata title=\"{EscapeMetadata(metadata.Title)}\"",
                $"-metadata artist=\"{EscapeMetadata(metadata.Artist)}\"",
                $"-metadata album=\"{EscapeMetadata(metadata.Album)}\"",
                $"-metadata date=\"{metadata.Year}\"",
                $"-metadata language=\"{EscapeMetadata(metadata.Language)}\""
            };

            return await ProcessAudioAsync(audioData, options => options
                .WithCustomArgument(string.Join(" ", metadataArgs))
                .WithCustomArgument("-c copy"), // Use stream copy to avoid re-encoding
                cancellationToken);
        }

        public async Task<byte[]> NormalizeVolumeAsync(byte[] audioData, float targetDb = -16.0f, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            _logger.LogInformation("Normalizing audio volume to {TargetDb}dB", targetDb);
            
            return await ProcessAudioAsync(audioData, options => options
                .WithCustomArgument($"-filter:a loudnorm=I={targetDb}:TP=-1.5:LRA=11:print_format=json")
                .WithAudioCodec("libmp3lame")
                .WithAudioBitrate(128),
                cancellationToken);
        }

        public async Task<byte[]> TrimSilenceAsync(byte[] audioData, float threshold = -50.0f, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            _logger.LogInformation("Trimming silence with threshold {Threshold}dB", threshold);
            
            return await ProcessAudioAsync(audioData, options => options
                .WithCustomArgument($"-af silenceremove=stop_periods=-1:stop_duration=1:stop_threshold={threshold}dB")
                .WithAudioCodec("libmp3lame")
                .WithAudioBitrate(128),
                cancellationToken);
        }

        public async Task<byte[]> CompressAudioAsync(byte[] audioData, AudioQuality quality, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            _logger.LogInformation("Compressing audio with quality {Quality}", quality);
            
            return await ProcessAudioAsync(audioData, options => options
                .WithAudioCodec("libmp3lame")
                .WithAudioBitrate((int)quality)
                .WithCustomArgument("-f mp3")
                .WithCustomArgument("-q:a 2"), // Use VBR encoding for better quality
                cancellationToken);
        }

        public Task<Stream> CreateStreamFromAudioAsync(byte[] audioData, int bufferSize = DefaultBufferSize, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var memoryStream = new MemoryStream(audioData);
            return Task.FromResult<Stream>(memoryStream);
        }

        public async Task<byte[]> ReadStreamToEndAsync(Stream audioStream, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(audioStream);

            using var memoryStream = new MemoryStream();
            await audioStream.CopyToAsync(memoryStream, DefaultBufferSize, cancellationToken);
            return memoryStream.ToArray();
        }

        private async Task<byte[]> ProcessAudioAsync(byte[] audioData, Action<FFMpegArgumentOptions> configureOptions, CancellationToken cancellationToken)
        {
            await _processingSemaphore.WaitAsync(cancellationToken);
            try
            {
                var inputFile = Path.Combine(Path.GetTempPath(), $"input_{Guid.NewGuid()}.tmp");
                var outputFile = Path.Combine(Path.GetTempPath(), $"output_{Guid.NewGuid()}.tmp");

                try
                {
                    await File.WriteAllBytesAsync(inputFile, audioData, cancellationToken);

                    var ffmpegArgs = FFMpegArguments
                        .FromFileInput(inputFile)
                        .OutputToFile(outputFile, false, configureOptions);

                    // Add progress logging
                    ffmpegArgs.NotifyOnProgress(progress =>
                    {
                        _logger.LogDebug("FFmpeg processing progress: {Progress}%", progress);
                    });

                    var success = await ffmpegArgs.ProcessAsynchronously();

                    if (!success)
                    {
                        throw new InvalidOperationException("FFmpeg processing failed");
                    }

                    return await File.ReadAllBytesAsync(outputFile, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing audio file");
                    throw;
                }
                finally
                {
                    SafeDeleteFile(inputFile);
                    SafeDeleteFile(outputFile);
                }
            }
            finally
            {
                _processingSemaphore.Release();
            }
        }

        private static string EscapeMetadata(string value)
        {
            return value.Replace("\"", "\\\"");
        }

        private static void SafeDeleteFile(string? filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception)
                {
                    // Ignore deletion errors for temp files
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AudioProcessingService));
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _processingSemaphore.Dispose();
            }
        }

        public async Task<IEnumerable<byte[]>> ProcessAudioBatchAsync(
            IEnumerable<byte[]> audioDataBatch,
            Func<byte[], CancellationToken, Task<byte[]>> processingFunc,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(audioDataBatch);
            ArgumentNullException.ThrowIfNull(processingFunc);

            var tasks = new List<Task<byte[]>>();
            var semaphore = new SemaphoreSlim(MaxConcurrentProcessing);

            try
            {
                foreach (var audioData in audioDataBatch)
                {
                    await semaphore.WaitAsync(cancellationToken);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            return await processingFunc(audioData, cancellationToken);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }

                return await Task.WhenAll(tasks);
            }
            finally
            {
                semaphore.Dispose();
            }
        }

        public async Task<IEnumerable<byte[]>> ConvertToMp3BatchAsync(
            IEnumerable<byte[]> audioDataBatch,
            CancellationToken cancellationToken)
        {
            return await ProcessAudioBatchAsync(
                audioDataBatch,
                (data, token) => ConvertToMp3Async(data, token),
                cancellationToken);
        }

        public async Task<IEnumerable<byte[]>> ConvertToM4aBatchAsync(
            IEnumerable<byte[]> audioDataBatch,
            CancellationToken cancellationToken)
        {
            return await ProcessAudioBatchAsync(
                audioDataBatch,
                (data, token) => ConvertToM4aAsync(data, token),
                cancellationToken);
        }

        public async Task<IEnumerable<byte[]>> ConvertToWavBatchAsync(
            IEnumerable<byte[]> audioDataBatch,
            CancellationToken cancellationToken)
        {
            return await ProcessAudioBatchAsync(
                audioDataBatch,
                (data, token) => ConvertToWavAsync(data, token),
                cancellationToken);
        }

        public async Task<IEnumerable<AudioMetadata>> ExtractMetadataBatchAsync(
            IEnumerable<byte[]> audioDataBatch,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(audioDataBatch);

            var tasks = new List<Task<AudioMetadata>>();
            var semaphore = new SemaphoreSlim(MaxConcurrentProcessing);

            try
            {
                foreach (var audioData in audioDataBatch)
                {
                    await semaphore.WaitAsync(cancellationToken);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            return await ExtractMetadataAsync(audioData, cancellationToken);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }

                return await Task.WhenAll(tasks);
            }
            finally
            {
                semaphore.Dispose();
            }
        }

        public async Task<IEnumerable<byte[]>> NormalizeVolumeBatchAsync(
            IEnumerable<byte[]> audioDataBatch,
            float targetDb = -16.0f,
            CancellationToken cancellationToken = default)
        {
            return await ProcessAudioBatchAsync(
                audioDataBatch,
                (data, token) => NormalizeVolumeAsync(data, targetDb, token),
                cancellationToken);
        }

        public async Task<IEnumerable<byte[]>> TrimSilenceBatchAsync(
            IEnumerable<byte[]> audioDataBatch,
            float threshold = -50.0f,
            CancellationToken cancellationToken = default)
        {
            return await ProcessAudioBatchAsync(
                audioDataBatch,
                (data, token) => TrimSilenceAsync(data, threshold, token),
                cancellationToken);
        }

        public async Task<IEnumerable<byte[]>> CompressAudioBatchAsync(
            IEnumerable<byte[]> audioDataBatch,
            AudioQuality quality,
            CancellationToken cancellationToken = default)
        {
            return await ProcessAudioBatchAsync(
                audioDataBatch,
                (data, token) => CompressAudioAsync(data, quality, token),
                cancellationToken);
        }
    }
} 