using MyTts.Models;
using MyTts.Repositories;

namespace MyTts.Services.Interfaces
{
    public interface ITtsManagerService : IAsyncDisposable
    {
        Task<(int id, AudioProcessor FileData)> ProcessContentAsync(
            string text, 
            int id, 
            string language, 
            AudioType fileType, 
            CancellationToken cancellationToken);

        Task<string> ProcessContentsAsync(
            IEnumerable<HaberSummaryDto> allNews,
            IEnumerable<HaberSummaryDto> neededNews,
            IEnumerable<HaberSummaryDto> savedNews,
            string language,
            AudioType fileType,
            CancellationToken cancellationToken = default);
    }
}