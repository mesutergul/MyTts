using MyTts.Models;

namespace MyTts.Services.Interfaces
{
    public interface IAudioProcessingService
    {
        #region Single File Operations
        
        // Audio file operations
        Task<byte[]> ConvertToMp3Async(byte[] audioData, CancellationToken cancellationToken);
        Task<byte[]> ConvertToM4aAsync(byte[] audioData, CancellationToken cancellationToken);
        Task<byte[]> ConvertToWavAsync(byte[] audioData, CancellationToken cancellationToken);
        
        // Audio metadata operations
        Task<AudioMetadata> ExtractMetadataAsync(byte[] audioData, CancellationToken cancellationToken);
        Task<byte[]> UpdateMetadataAsync(byte[] audioData, AudioMetadata metadata, CancellationToken cancellationToken);
        
        // Audio processing operations
        Task<byte[]> NormalizeVolumeAsync(byte[] audioData, float targetDb = -16.0f, CancellationToken cancellationToken = default);
        Task<byte[]> TrimSilenceAsync(byte[] audioData, float threshold = -50.0f, CancellationToken cancellationToken = default);
        Task<byte[]> CompressAudioAsync(byte[] audioData, AudioQuality quality, CancellationToken cancellationToken = default);
        
        // Stream processing
        Task<Stream> CreateStreamFromAudioAsync(byte[] audioData, int bufferSize = 81920, CancellationToken cancellationToken = default);
        Task<byte[]> ReadStreamToEndAsync(Stream audioStream, CancellationToken cancellationToken = default);

        #endregion

        #region Batch Operations

        // Batch audio file operations
        Task<IEnumerable<byte[]>> ConvertToMp3BatchAsync(IEnumerable<byte[]> audioDataBatch, CancellationToken cancellationToken);
        Task<IEnumerable<byte[]>> ConvertToM4aBatchAsync(IEnumerable<byte[]> audioDataBatch, CancellationToken cancellationToken);
        Task<IEnumerable<byte[]>> ConvertToWavBatchAsync(IEnumerable<byte[]> audioDataBatch, CancellationToken cancellationToken);
        
        // Batch metadata operations
        Task<IEnumerable<AudioMetadata>> ExtractMetadataBatchAsync(IEnumerable<byte[]> audioDataBatch, CancellationToken cancellationToken);
        
        // Batch audio processing operations
        Task<IEnumerable<byte[]>> NormalizeVolumeBatchAsync(IEnumerable<byte[]> audioDataBatch, float targetDb = -16.0f, CancellationToken cancellationToken = default);
        Task<IEnumerable<byte[]>> TrimSilenceBatchAsync(IEnumerable<byte[]> audioDataBatch, float threshold = -50.0f, CancellationToken cancellationToken = default);
        Task<IEnumerable<byte[]>> CompressAudioBatchAsync(IEnumerable<byte[]> audioDataBatch, AudioQuality quality, CancellationToken cancellationToken = default);

        #endregion
    }
} 