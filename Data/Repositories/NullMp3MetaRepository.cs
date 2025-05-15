// Null fallback for Mp3MetaRepository
using MyTts.Data.Entities;


using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using MyTts.Data.Interfaces;
using Microsoft.Extensions.Logging;
using MyTts.Data.Context;
using MyTts.Models;

namespace MyTts.Data.Repositories
{
    public class NullMp3MetaRepository : Mp3MetaRepository
    {
        public NullMp3MetaRepository(
             IGenericDbContextFactory<AppDbContext> factory, // Inject IAppDbContextFactory
             IMapper mapper,               // Inject IMapper
             ILogger<NullMp3MetaRepository> logger)
             : base(factory, mapper, logger) // Pass the injected dependencies to the base
        {
        }

        public override Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public override Task<List<Mp3Dto>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<List<Mp3Dto>>(new List<Mp3Dto>());
        }

        public override Task<Mp3Dto> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            return Task.FromResult<Mp3Dto>(null);
        }

        public override Task AddAsync(Mp3Dto entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task Update(Mp3Dto entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override Task Delete(Mp3Dto entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}