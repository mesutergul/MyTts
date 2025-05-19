using Microsoft.EntityFrameworkCore;
using MyTts.Data.Context;
using MyTts.Data.Interfaces;
using MyTts.Data.Entities;
using AutoMapper; // Ensure the namespace containing BaseEntity is imported
using System.Linq.Expressions;

namespace MyTts.Data.Repositories
{
    public class Repository<TContext, TEntity, TModel> : IRepository<TEntity, TModel>
        where TContext : DbContext
        where TEntity : BaseEntity
        where TModel : class
    {
        protected readonly TContext _context;
        protected readonly DbSet<TEntity> _dbSet;
        protected readonly IMapper _mapper;
        protected readonly ILogger<Repository<TContext, TEntity, TModel>> _logger;

        public Repository(IGenericDbContextFactory<TContext> factory, IMapper mapper, ILogger<Repository<TContext, TEntity, TModel>> logger)
        {
            _context = factory.CreateDbContext() ?? throw new ArgumentNullException(nameof(factory), "AppDbContextFactory cannot be null");
            _dbSet = _context.Set<TEntity>() ?? throw new ArgumentNullException(nameof(_context), "AppDbContext cannot be null");
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper), "Mapper cannot be null");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger), "Logger cannot be null");
        }

        public virtual async Task<List<TModel>> GetAllAsync(CancellationToken cancellationToken)
        {
            var entities = await _dbSet.ToListAsync();
            return _mapper.Map<List<TModel>>(entities) ?? throw new InvalidOperationException("Mapping failed");
        }

        public virtual async Task<TModel> GetByIdAsync(int id, CancellationToken cancellationToken)
        {
            var entity = await _dbSet.FindAsync(id);
            return _mapper.Map<TModel>(entity) ?? throw new KeyNotFoundException($"Entity with id {id} not found");
        }

        public virtual async Task AddAsync(TModel model, CancellationToken cancellationToken)
        {
            var entity = _mapper.Map<TEntity>(model);
            await _dbSet.AddAsync(entity);
            await SaveChangesAsync(cancellationToken);
        }

        public virtual async Task Update(TModel model, CancellationToken cancellationToken)
        {
            var entity = _mapper.Map<TEntity>(model);
            _dbSet.Update(entity);
            await SaveChangesAsync(cancellationToken);
        }

        public virtual async Task Delete(TModel model, CancellationToken cancellationToken)
        {
            var entity = _mapper.Map<TEntity>(model);
            _dbSet.Remove(entity);
            await SaveChangesAsync(cancellationToken);
        }

        public virtual async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        public virtual async Task<TModel> FindAsync(Func<TEntity, bool> predicate, CancellationToken cancellationToken)
        {
            var result = await Task.Run(() => _dbSet.FirstOrDefault(predicate), cancellationToken);
            return _mapper.Map<TModel>(result) ?? throw new KeyNotFoundException("Entity not found matching predicate");
        }

        protected virtual async Task<TModel> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken)
        {
            var result = await _dbSet.FirstOrDefaultAsync(predicate, cancellationToken);
            return _mapper.Map<TModel>(result) ?? throw new KeyNotFoundException("Entity not found matching predicate");
        }

        public virtual async Task DeleteAsync(int id, CancellationToken cancellationToken)
        {
            var entity = await _dbSet.FindAsync(id);
            if (entity != null)
            {
                _dbSet.Remove(entity);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
