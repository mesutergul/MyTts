using MyTts.Models;
using MyTts.Repositories;

namespace MyTts.Services{
    public interface ITtsManagerService
    {
        Task<(Stream audioData, string contentType, string fileName)> ProcessContentsAsync(
        IEnumerable<HaberSummaryDto> allNews, IEnumerable<HaberSummaryDto> neededNews, string language, AudioType fileType, CancellationToken cancellationToken = default);
        Task<(string LocalPath, AudioProcessor FileData)> ProcessContentAsync(
            string text, int id, string language, AudioType fileType, CancellationToken cancellationToken  = default);
    }
}