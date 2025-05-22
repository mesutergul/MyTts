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

            // Use a simpler query with direct IN clause
            var query = @"
                SELECT TOP(500) [h].[haber_id]
                FROM [Haber_Ses_Dosyalari] AS [h]
                WHERE [h].[haber_id] IN ({0})
                ORDER BY [h].[id] DESC";

            // Create parameters for the IN clause
            var parameters = new List<object>();
            var paramNames = new List<string>();
            
            for (int i = 0; i < fileIdsToCheck.Count; i++)
            {
                var paramName = $"@p{i}";
                paramNames.Add(paramName);
                parameters.Add(fileIdsToCheck[i]);
            }

            var paramList = string.Join(",", paramNames);
            var formattedQuery = string.Format(query, paramList);

            return await _context.Database
                .SqlQueryRaw<int>(formattedQuery, parameters.ToArray())
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
        public async Task AddRangeAsync(IEnumerable<Mp3Dto> entities, IEnumerable<Mp3Dto> updateEntities, CancellationToken cancellationToken = default)
        {
            try
            {
                var newRecords = entities.ToList();
                var upRecords = updateEntities.ToList();

                // Add new records
                if (newRecords.Any())
                {
                    var newEntities = newRecords.Select(x => _mapper.Map<Mp3Meta>(x));
                    await _dbSet.AddRangeAsync(newEntities, cancellationToken);
                }

                // Update existing records
                if (upRecords.Any())
                {
                    foreach (var dto in upRecords)
                    {
                        var dentity = await _dbSet.FirstOrDefaultAsync(x => x.FileId == dto.FileId, cancellationToken);
                        if (dentity != null)
                        {
                            dentity.FileUrl = dto.FileUrl;
                            dentity.Language = dto.Language;
                        }
                        else
                        {
                            await _dbSet.AddAsync(_mapper.Map<Mp3Meta>(dto));
                        }
                    }                 
                }

                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully processed {NewCount} new records and {UpdateCount} updates", 
                    newRecords.Count, upRecords.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding MP3 files to database");
                throw;
            }
        }
    }
}