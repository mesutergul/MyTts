using MyTts.Data.Entities;
using MyTts.Models;

namespace MyTts.Data.Interfaces
{
    public interface INewsRepository : IRepository<News, INews>
    {
        Task<List<HaberSummaryDto>> getSummary(int top, MansetType mansetType, CancellationToken cancellationToken);
    }
}
