using MyTts.Data.Context;
using MyTts.Data.Interfaces;
using AutoMapper;
using MyTts.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using MyTts.Data.Entities;

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
        public virtual async Task<Mp3Meta> GetByColumnAsync(
            Expression<Func<Mp3Meta, bool>> predicate,
            CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogWarning("DbSet is not available. Skipping column-based find.");
                return null;
            }

            var entity = await _dbSet.FirstOrDefaultAsync(predicate, cancellationToken);
            return entity ?? throw new InvalidOperationException($"Entity not found matching predicate.");
        }


    }
}