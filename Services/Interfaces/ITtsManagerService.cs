using MyTts.Models;
using MyTts.Repositories;

namespace MyTts.Services.Interfaces{
    public interface ITtsManagerService
    {
        Task<(Stream audioData, string contentType, string fileName)> ProcessContentsAsync(
        IEnumerable<HaberSummaryDto> allNews, IEnumerable<HaberSummaryDto> neededNews, IEnumerable<HaberSummaryDto> savedNews, string language, AudioType fileType, CancellationToken cancellationToken = default);
        Task<(int id, AudioProcessor FileData)> ProcessContentAsync(
            string text, int id, string language, AudioType fileType, CancellationToken cancellationToken  = default);
    }
}