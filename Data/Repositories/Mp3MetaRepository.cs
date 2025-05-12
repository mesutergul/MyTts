using MyTts.Data.Context;
using MyTts.Data.Interfaces;
using AutoMapper;
using MyTts.Models;

namespace MyTts.Data.Repositories
{
    public class Mp3MetaRepository : Repository<Entities.Mp3Meta, Interfaces.IMp3>, IMp3MetaRepository
    {
        public Mp3MetaRepository(IAppDbContextFactory contextFactory, IMapper? mapper, ILogger<Mp3MetaRepository> logger) : base(contextFactory, mapper, logger) { }

        // Mp3File'a özel metodları burada implemente edebilirsin
        
        public virtual async Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken)
        {
            if (_context == null || _dbSet == null)
            {
                _logger.LogWarning("SQL not available for ExistById check");
                return false;
            }

            return await Task.FromResult(_dbSet.Any(entity => entity.FileId == id));
        }

    }
}