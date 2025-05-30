using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Models;
using MyTts.Helpers;

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
        
        public Task<List<HaberSummaryDto>> getSummary(int count, MansetType type, CancellationToken cancellationToken)
        {
            try
            {
                var summaries = CsvFileReader.ReadHaberSummariesFromCsv(StoragePathHelper.GetFullPath("test", AudioType.Csv))
                    .Select(x => new HaberSummaryDto() { Baslik = x.Baslik, IlgiId = x.IlgiId, Ozet = x.Ozet })
                    .Take(count)
                    .ToList();
                
                return Task.FromResult(summaries);
            }
            catch (Exception)
            {
                return Task.FromResult(new List<HaberSummaryDto>());
            }
        }

        Task<NewsDto> IRepository<News, NewsDto>.GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<List<NewsDto>> IRepository<News, NewsDto>.GetAllAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IRepository<News, NewsDto>.AddAsync(NewsDto model, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task<NewsDto> IRepository<News, NewsDto>.FindAsync(Func<News, bool> predicate, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IRepository<News, NewsDto>.Update(NewsDto model, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        Task IRepository<News, NewsDto>.DeleteAsync(int id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
