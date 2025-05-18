using MyTts.Models;

namespace MyTts.Services.Interfaces
{
    public interface IMp3StreamMerger{
        Task<(Stream audioData, string contentType, string fileName)> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            string? breakAudioPath = null,
            CancellationToken cancellationToken = default);
    }
}