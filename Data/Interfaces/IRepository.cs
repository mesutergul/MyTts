using System.Collections.Generic;
using System.Threading.Tasks;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Interfaces
{
    public interface IRepository<TEntity, TModel> 
    
    where TEntity : BaseEntity 
    where TModel : class
    {
        Task<TModel> GetByIdAsync(int id, CancellationToken cancellationToken);
        Task<List<TModel>> GetAllAsync(CancellationToken cancellationToken);
        Task AddAsync(TModel model, CancellationToken cancellationToken);
        Task SaveChangesAsync(CancellationToken cancellationToken);
        Task<TModel> FindAsync(Func<TEntity, bool> predicate, CancellationToken cancellationToken);
        Task Update(TModel model, CancellationToken cancellationToken);
        Task DeleteAsync(int id, CancellationToken cancellationToken);
    }
    
}
