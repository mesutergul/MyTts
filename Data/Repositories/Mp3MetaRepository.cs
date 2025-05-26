using Microsoft.EntityFrameworkCore;
using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Models;
using AutoMapper;
using System.Linq.Expressions;
using System.Data;

namespace MyTts.Data.Repositories
{
    public class Mp3MetaRepository : IMp3MetaRepository
    {
        private readonly AppDbContext _context;
        private readonly DbSet<Mp3Meta> _dbSet;
        private readonly ILogger<Mp3MetaRepository> _logger;
        private readonly IMapper _mapper;

        public Mp3MetaRepository(
            AppDbContext context,
            ILogger<Mp3MetaRepository> logger,
            IMapper mapper)
        {
            _context = context;
            _dbSet = context.Set<Mp3Meta>();
            _logger = logger;
            _mapper = mapper;
        }

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

            return await _dbSet
                .OrderByDescending(x => x.Id)
                .Take(500)
                .Where(x => fileIdsToCheck.Contains(x.FileId))  
                .Select(x => x.FileId)             
                .ToListAsync(cancellationToken);
        }

        public async Task<Mp3Dto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var entity = await _dbSet.AsNoTracking().FirstOrDefaultAsync(e => e.FileId == id, cancellationToken);
            return entity != null ? _mapper.Map<Mp3Dto>(entity) : throw new KeyNotFoundException($"MP3 with ID {id} not found.");
        }

        public async Task<List<Mp3Dto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var entities = await _dbSet.ToListAsync(cancellationToken);
            return _mapper.Map<List<Mp3Dto>>(entities);
        }

        public async Task AddAsync(Mp3Dto dto, CancellationToken cancellationToken = default)
        {
            var entity = _mapper.Map<Mp3Meta>(dto);
            await _dbSet.AddAsync(entity, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<Mp3Dto> FindAsync(Func<Mp3Meta, bool> predicate, CancellationToken cancellationToken = default)
        {
            var entity = await _dbSet.AsNoTracking().FirstOrDefaultAsync(m => predicate(m), cancellationToken);
            return entity != null ? _mapper.Map<Mp3Dto>(entity) : throw new KeyNotFoundException("Entity not found.");
        }
        public async Task Update(Mp3Dto dto, CancellationToken cancellationToken = default)
        {
            var entity = await _dbSet.FindAsync(new object[] { dto.FileId }, cancellationToken);
            if (entity != null)
            {
                _mapper.Map(dto, entity);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var entity = await _dbSet.FindAsync(new object[] { id }, cancellationToken);
            if (entity != null)
            {
                _dbSet.Remove(entity);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        public async Task UpdateRangeAsync(IEnumerable<Mp3Dto> dtos, CancellationToken cancellationToken = default)
        {
            var newRecords = dtos.ToList();
            if (newRecords.Any())
            {
                var entities = newRecords.Select(x => _mapper.Map<Mp3Meta>(x));
                _dbSet.UpdateRange(entities);
            }
            await _context.SaveChangesAsync(cancellationToken);
        }
        public async Task AddRangeAsync(List<Mp3Dto> allRecords, CancellationToken cancellationToken = default)
        {
            if (allRecords == null || !allRecords.Any())
            {
                _logger.LogWarning("No records provided to AddRangeAsync");
                return;
            }

            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                    try
                    {
                        var idRecords = allRecords.Select(x => x.FileId).ToList();
                        var existingEntities = await GetExistingFilesInLast100Async(idRecords, cancellationToken);
                        var newRecords = allRecords.Where(x => !existingEntities.Any(y => y.FileId == x.FileId)).ToList();
                        Dictionary<int, Mp3Dto> updateRecords = allRecords.Where(x => existingEntities.Any(y => y.FileId == x.FileId)).ToDictionary(x => x.FileId);

                        // Add new records
                        if (newRecords.Any())
                        {
                            var newEntities = newRecords.Select(x => _mapper.Map<Mp3Meta>(x));
                            await _dbSet.AddRangeAsync(newEntities, cancellationToken);
                            _logger.LogInformation("Adding {Count} new records", newRecords.Count);
                        }

                        // Update existing records
                        if (existingEntities.Any())
                        {
                            foreach (var meta in existingEntities)
                            {
                                if (updateRecords.TryGetValue(meta.FileId, out var updateRecord))
                                {
                                    meta.FileUrl = updateRecord.FileUrl;
                                    meta.Language = updateRecord.Language;
                                }
                            }
                            _logger.LogInformation("Updating {Count} existing records", existingEntities.Count);
                        }

                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        
                        _logger.LogInformation("Successfully processed {NewCount} new records and {UpdateCount} updates",
                            newRecords.Count, updateRecords.Count);
                    }
                    catch (OperationCanceledException)
                    {
                        try
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                        }
                        catch (Exception rollbackEx)
                        {
                            _logger.LogWarning(rollbackEx, "Error during transaction rollback after cancellation");
                        }
                        _logger.LogInformation("Operation was cancelled");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                        }
                        catch (Exception rollbackEx)
                        {
                            _logger.LogWarning(rollbackEx, "Error during transaction rollback");
                        }
                        _logger.LogError(ex, "Error during database transaction");
                        throw;
                    }
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation was cancelled while adding MP3 files");
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database update error while adding MP3 files");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while adding MP3 files to database");
                throw;
            }
        }
         public async Task<List<Mp3Meta>> GetExistingFilesInLast100Async(List<int> fileIdsToCheck, CancellationToken cancellationToken)
        {
            if (fileIdsToCheck == null || !fileIdsToCheck.Any())
            {
                return new List<Mp3Meta>();
            }

            var result = new List<Mp3Meta>();
            foreach (var id in fileIdsToCheck)
            {
                var entity = await _dbSet
                    .Where(x => x.FileId == id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (entity != null)
                {
                    result.Add(entity);
                }
            }

            return result;
        }
        public async Task<Dictionary<int, string>> GetExistingHashesAsync(List<int> fileIdsToCheck, CancellationToken cancellationToken)
        {
            if (fileIdsToCheck == null || !fileIdsToCheck.Any())
            {
                return new Dictionary<int, string>();
            }

            var result = new Dictionary<int, string>();
            foreach (var id in fileIdsToCheck)
            {
                var exists = await _dbSet
                    .Where(x => x.FileId == id)
                    .Select(x => x.OzetHash)
                    .FirstOrDefaultAsync(cancellationToken);

                if (exists != null)
                {
                    result.Add(id, exists);
                }
            }

            return result;
        }
    }

}