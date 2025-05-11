using System.Collections.Generic;
using System.Threading.Tasks;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Interfaces
{
    public interface IRepository<TEntity, TModel> 
    
    where TEntity : BaseEntity 
    where TModel : class, IModel
    {
        Task<TEntity> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken);
        Task AddAsync(TEntity entity, CancellationToken cancellationToken);
        Task SaveChangesAsync(CancellationToken cancellationToken);
        Task<IEnumerable<TEntity>> FindAsync(Func<TEntity, bool> predicate, CancellationToken cancellationToken);
        void Update(TEntity entity, CancellationToken cancellationToken);
        void Delete(TEntity entity, CancellationToken cancellationToken);
    }
    
}
