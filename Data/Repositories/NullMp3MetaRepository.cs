// Null fallback for Mp3MetaRepository
using MyTts.Data.Entities;


using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using MyTts.Data.Interfaces;
using Microsoft.Extensions.Logging;

namespace MyTts.Data.Repositories
{
    public class NullMp3MetaRepository : Mp3MetaRepository
    {
        public NullMp3MetaRepository(
             IAppDbContextFactory factory, // Inject IAppDbContextFactory
             IMapper mapper,               // Inject IMapper
             ILogger<NullMp3MetaRepository> logger)
             : base(factory, mapper, logger) // Pass the injected dependencies to the base
        {
        }

        public override Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public override Task<IEnumerable<Mp3Meta>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Mp3Meta>>(new List<Mp3Meta>());
        }

        public override Task<Mp3Meta> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Mp3Meta>(null);
        }

        public override Task AddAsync(Mp3Meta entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task Update(Mp3Meta entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task Delete(Mp3Meta entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}