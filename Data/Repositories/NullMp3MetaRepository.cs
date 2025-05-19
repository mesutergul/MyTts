// Null fallback for Mp3MetaRepository
using MyTts.Data.Entities;


using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using MyTts.Data.Interfaces;
using Microsoft.Extensions.Logging;
using MyTts.Data.Context;
using MyTts.Models;
using System.Linq.Expressions;

namespace MyTts.Data.Repositories
{
    public class NullMp3MetaRepository : IMp3MetaRepository
    {
        // public NullMp3MetaRepository(
        //      IGenericDbContextFactory<AppDbContext> factory, // Inject IAppDbContextFactory
        //      IMapper mapper,               // Inject IMapper
        //      ILogger<NullMp3MetaRepository> logger)
        //      : base(factory, mapper, logger) // Pass the injected dependencies to the base
        // {
        // }

        public  Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public  Task<List<Mp3Dto>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<List<Mp3Dto>>(new List<Mp3Dto>());
        }

        public  Task<Mp3Dto> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Mp3Dto>(null!);
        }

        public  Task AddAsync(Mp3Dto entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public  Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public  Task Update(Mp3Dto entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public  Task Delete(Mp3Dto entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
        public Task<List<int>> GetExistingFileIdsInLast500Async(List<int> fileIdsToCheck, CancellationToken cancellationToken) =>Task.FromResult( new List<int>());
        Task<Mp3Dto> GetByColumnAsync(
            Expression<Func<Mp3Meta, bool>> predicate,
            CancellationToken cancellationToken) => Task.FromResult<Mp3Dto>(default!);

        Task<Mp3Dto> IMp3MetaRepository.GetByColumnAsync(Expression<Func<Mp3Meta, bool>> predicate, CancellationToken cancellationToken)
        {
            return GetByColumnAsync(predicate, cancellationToken);
        }

        Task<Mp3Dto> IRepository<Mp3Meta, Mp3Dto>.FindAsync(Func<Mp3Meta, bool> predicate, CancellationToken cancellationToken)
        {
            return Task.FromResult<Mp3Dto>(default!);
        }

        Task IRepository<Mp3Meta, Mp3Dto>.DeleteAsync(int id, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        Task IMp3MetaRepository.AddRangeAsync(IEnumerable<Mp3Dto> entities, CancellationToken cancellationToken)
        {
            // This method is not needed for the null implementation
            return Task.CompletedTask;
        }
    }
}