using MyTts.Data.Context;
using MyTts.Data.Interfaces;
using AutoMapper;
using MyTts.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using MyTts.Data.Entities;
using System.Data;

namespace MyTts.Data.Repositories
{
    public class Mp3MetaRepository : Repository<AppDbContext,Mp3Meta, Mp3Dto>, IMp3MetaRepository
    {
        public Mp3MetaRepository(IGenericDbContextFactory<AppDbContext> contextFactory, IMapper mapper, ILogger<Mp3MetaRepository> logger) : base(contextFactory, mapper, logger) { }

        // Mp3File'a özel metodları burada implemente edebilirsin

        public virtual async Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await Task.FromResult(_dbSet.Any(entity => entity.FileId == id));
        }
        public virtual async Task<Mp3Dto> GetByColumnAsync(
            Expression<Func<Mp3Meta, bool>> predicate,
            CancellationToken cancellationToken)
        {
            var entity = await _dbSet.FirstOrDefaultAsync(predicate, cancellationToken);
            return _mapper.Map<Mp3Dto>(entity) ?? throw new InvalidOperationException($"Entity not found matching predicate.");
        }
        public async Task<List<int>> GetExistingFileIdsInLast500Async(List<int> fileIdsToCheck, CancellationToken cancellationToken)
        {
            if (fileIdsToCheck == null || !fileIdsToCheck.Any())
            {
                return new List<int>();
            }

            // Create a comma-separated list of IDs
            var idList = string.Join(",", fileIdsToCheck);
            var query = $@"
                SELECT [h0].[haber_id]
                FROM (
                    SELECT TOP(500) [h].[id], [h].[haber_id]
                    FROM [Haber_Ses_Dosyalari] AS [h]
                    ORDER BY [h].[id] DESC
                ) AS [h0]
                WHERE [h0].[haber_id] IN ({idList})
                ORDER BY [h0].[id] DESC";

            return await _context.Database
                .SqlQueryRaw<int>(query)
                .ToListAsync(cancellationToken);
        }

        public async Task AddRangeAsync(IEnumerable<Mp3Dto> entities, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entities);

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
                try
                {
                    // Map DTOs to entities
                    var mappedEntities = entities.Select(dto => _mapper.Map<Mp3Meta>(dto)).ToList();
                    
                    // Add the mapped entities
                    await _dbSet.AddRangeAsync(mappedEntities, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    _logger.LogInformation("Successfully saved batch of {Count} entities", mappedEntities.Count);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Failed to save batch of {Count} entities", entities.Count());
                    throw;
                }
            });
        }
    }
}