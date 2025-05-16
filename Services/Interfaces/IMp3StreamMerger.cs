using MyTts.Repositories;

namespace MyTts.Services.Interfaces
{
    public interface IMp3StreamMerger{
        Task<(Stream audioData, string contentType, string fileName)> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            CancellationToken cancellationToken = default);
    }
}