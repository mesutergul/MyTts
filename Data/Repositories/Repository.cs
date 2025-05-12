using Microsoft.EntityFrameworkCore;
using MyTts.Data.Context;
using MyTts.Data.Interfaces;
using MyTts.Data.Entities;
using AutoMapper; // Ensure the namespace containing BaseEntity is imported

namespace MyTts.Data.Repositories
{
    public class Repository<TEntity, TModel> : IRepository<TEntity, TModel>
    where TEntity : BaseEntity
    where TModel : class, IModel

    {
        protected readonly AppDbContext? _context;
        protected readonly DbSet<TEntity>? _dbSet;
        protected readonly IMapper? _mapper;
        protected readonly ILogger<Repository<TEntity, TModel>> _logger;

        public Repository(IAppDbContextFactory factory, IMapper? mapper, ILogger<Repository<TEntity, TModel>> logger)
        {
            _context = factory.Create()?? throw new ArgumentNullException(nameof(factory), "AppDbContextFactory cannot be null");
            _dbSet = _context?.Set<TEntity>() ?? throw new ArgumentNullException(nameof(_context), "AppDbContext cannot be null");
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper), "Mapper cannot be null");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger), "Logger cannot be null");
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogWarning("DbSet is not available — skipping GetAllAsync.");
                return Enumerable.Empty<TEntity>();
            }

            return await _dbSet.ToListAsync();
        }

        public virtual async Task<TEntity?> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogWarning("DbSet is not available. Skipping GetById check.");
                return null;
            }
            var entity = await _dbSet.FindAsync(id);
            return entity ?? throw new InvalidOperationException($"Entity with id {id} not found");
        }

        public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken)
        {
            if (_dbSet == null || _context == null)
            {
                _logger.LogError("Cannot add entity — database context is not available.");
                return;
            }
            await _dbSet.AddAsync(entity);
            await SaveChangesAsync(cancellationToken);
        }

        public virtual async Task Update(TEntity entity, CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogError("Cannot update entity — database context is not available.");
                return;
            }
            _dbSet.Update(entity);
            await SaveChangesAsync(cancellationToken);
        }

        public virtual async Task Delete(TEntity entity, CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogError("Cannot update entity — database context is not available.");
                return;
            }
            _dbSet.Remove(entity);
            await SaveChangesAsync(cancellationToken);
        }

        public virtual async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (_context == null)
            {
                _logger.LogError("Cannot save changes — database context is not available.");
                return;
            }
            await _context.SaveChangesAsync(cancellationToken);
        }

        public virtual async Task<TEntity?> FindAsync(Func<TEntity, bool> predicate, CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogWarning("DbSet is not available — skipping FindAsync.");
                return null;
            }

            var result = _dbSet.FirstOrDefault(predicate);
            return await Task.FromResult(result ?? null);
        }

        Task<IEnumerable<TEntity>> IRepository<TEntity, TModel>.FindAsync(Func<TEntity, bool> predicate, CancellationToken cancellationToken)
        {
            if (_dbSet == null)
            {
                _logger.LogWarning("DbSet is not available — skipping FindAsync.");
                return Task.FromResult(Enumerable.Empty<TEntity>());
            }

            return Task.FromResult(_dbSet.Where(predicate).AsEnumerable());
        }

    }
}
