using MyTts.Data.Context;
using MyTts.Data.Interfaces;
using AutoMapper;
using MyTts.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using MyTts.Data.Entities;

namespace MyTts.Data.Repositories
{
    public class Mp3MetaRepository : Repository<AppDbContext,Mp3Meta, Mp3Dto>, IMp3MetaRepository
    {
        public Mp3MetaRepository(IGenericDbContextFactory<AppDbContext> contextFactory, IMapper? mapper, ILogger<Mp3MetaRepository> logger) : base(contextFactory, mapper, logger) { }

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
        public virtual async Task<Mp3Dto> GetByColumnAsync(
            Expression<Func<Mp3Meta, bool>> predicate,
            CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogWarning("DbSet is not available. Skipping column-based find.");
                return null;
            }

            var entity = await _dbSet.FirstOrDefaultAsync(predicate, cancellationToken);
            return _mapper.Map<Mp3Dto>(entity) ?? throw new InvalidOperationException($"Entity not found matching predicate.");
        }
        public virtual async Task<List<int>> GetExistingFileIdsInLast500Async(List<int> fileIdsToCheck, CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogWarning("DbSet is not available. Cannot check for FileIds in last 500 records.");
                return new List<int>(); // Return an empty list if DbSet is not available
            }

            if (fileIdsToCheck == null || !fileIdsToCheck.Any())
            {
                _logger.LogInformation("No FileIds provided to check.");
                return new List<int>(); // Return empty list if no IDs to check
            }

            // Use LINQ to Entities to build the query
            var existingIds = await _dbSet
                .OrderByDescending(e => e.Id) // Order by the primary key (auto-incrementing) descending
                .Take(500) // Take the last 500 records
                .Where(e => fileIdsToCheck.Contains(e.FileId)) // Filter to include only records whose FileId is in the provided list
                .Select(e => e.FileId) // Select only the FileId
                .ToListAsync(cancellationToken); // Execute the query asynchronously and get the results as a list

            return existingIds;
        }

    }
}