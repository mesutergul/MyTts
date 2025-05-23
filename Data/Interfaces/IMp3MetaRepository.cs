using MyTts.Data.Entities;
using MyTts.Models;
using System.Linq.Expressions;

namespace MyTts.Data.Interfaces
{
    public interface IMp3MetaRepository : IRepository<Mp3Meta, Mp3Dto>
    {
        Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken);
        Task<List<int>> GetExistingFileIdsInLast500Async(List<int> values, CancellationToken cancellationToken);
        Task<Mp3Dto> GetByColumnAsync(Expression<Func<Mp3Meta, bool>> predicate, CancellationToken cancellationToken);
        Task AddRangeAsync(List<Mp3Dto> newEntities, CancellationToken cancellationToken);
        Task<Dictionary<int, string>> GetExistingHashesAsync(List<int> values, CancellationToken cancellationToken);
    }
}
