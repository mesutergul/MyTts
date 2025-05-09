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

        public override Task<bool> ExistByIdAsync(int id)
        {
            return Task.FromResult(false);
        }

        public override Task<IEnumerable<Mp3Meta>> GetAllAsync() // Added override
        {
            return Task.FromResult<IEnumerable<Mp3Meta>>(new List<Mp3Meta>());
        }

        public override Task<Mp3Meta> GetByIdAsync(int id) // Added override
        {
            return Task.FromResult<Mp3Meta>(null);
        }

        public override Task AddAsync(Mp3Meta entity) // Added override
        {
            return Task.CompletedTask;
        }

        public override Task SaveChangesAsync() // Added override
        {
            return Task.CompletedTask;
        }

        public override void Update(Mp3Meta entity) { } // Added override

        public override void Delete(Mp3Meta entity) { } // Added override
    }
}