using MyTts.Data.Entities;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Repositories
{
    public class NullNewsRepository : IRepository<News, INews>
    {
        public Task AddAsync(News entity, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Delete(News entity, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IEnumerable<News>> FindAsync(Func<News, bool> predicate, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<News>());

        public Task<IEnumerable<News>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<News>());
        public Task<News> GetByIdAsync(int id, CancellationToken cancellationToken) => Task.FromResult<News>(null);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Update(News entity, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
