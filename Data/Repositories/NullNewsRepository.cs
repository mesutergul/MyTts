using AutoMapper;
using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Models;

namespace MyTts.Data.Repositories
{
    public class NullNewsRepository : INewsRepository
    {
        // public NullNewsRepository(
        //          IGenericDbContextFactory<AppDbContext> factory, // Inject IAppDbContextFactory
        //          IMapper mapper,               // Inject IMapper
        //          ILogger<NullNewsRepository> logger)
        //          : base(factory, mapper, logger) // Pass the injected dependencies to the base
        // {
        // }
        public Task AddAsync(News entity, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Delete(News entity, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<List<News>> FindAsync(Func<News, bool> predicate, CancellationToken cancellationToken) => Task.FromResult(new List<News>());

        public Task<List<News>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(new List<News>());
        public Task<News> GetByIdAsync(int id, CancellationToken cancellationToken) => Task.FromResult<News>(null!);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Update(News entity, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<List<HaberSummaryDto>> getSummary(int count, MansetType type, CancellationToken cancellationToken) => Task.FromResult(new List<HaberSummaryDto>());

        Task<INews> IRepository<News, INews>.GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<List<INews>> IRepository<News, INews>.GetAllAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IRepository<News, INews>.AddAsync(INews model, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<INews> IRepository<News, INews>.FindAsync(Func<News, bool> predicate, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IRepository<News, INews>.Update(INews model, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IRepository<News, INews>.DeleteAsync(int id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
