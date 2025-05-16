using AutoMapper;
using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Repositories
{
    public class AppRepository<TEntity, TModel>
   : Repository<AppDbContext, TEntity, TModel>,
     IRepository<TEntity, TModel>
   where TEntity : BaseEntity
   where TModel : class
    {
        public AppRepository(
          IGenericDbContextFactory<AppDbContext> factory,
          IMapper mapper,
          ILogger<AppRepository<TEntity, TModel>> logger)
          : base(factory, mapper, logger)
        { }
    }

}
