using Microsoft.EntityFrameworkCore;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Context
{
    public class NullGenericDbContextFactory<TContext> : IGenericDbContextFactory<TContext>
     where TContext : DbContext
    {
        private readonly DbContextOptions<TContext> _options;
        public NullGenericDbContextFactory()
        {
            // build in-memory options for any TContext
            _options = new DbContextOptionsBuilder<TContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        }
        public TContext CreateDbContext()
        {
            // assumes TContext has a ctor(DbContextOptions<TContext>);
            return (TContext)Activator.CreateInstance(
                typeof(TContext), _options
            )!;
        }
    }

}
