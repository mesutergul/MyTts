using MyTts.Models;

namespace MyTts.Services.Interfaces
{
    public interface IMp3StreamMerger : IAsyncDisposable
    {
        Task<string> MergeMp3ByteArraysAsync(
            IReadOnlyList<AudioProcessor> audioProcessors,
            string basePath,
            AudioType fileType,
            string breakAudioPath,
            string startAudioPath,
            string endAudioPath,
            CancellationToken cancellationToken = default);
    }
}