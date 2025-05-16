using Microsoft.EntityFrameworkCore;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Context
{
    public class GenericDbContextFactory<TContext> : IGenericDbContextFactory<TContext>
    where TContext : DbContext
    {
        private readonly IDbContextFactory<TContext> _efFactory;

        public GenericDbContextFactory(IDbContextFactory<TContext> efFactory)
        {
            _efFactory = efFactory
                ?? throw new ArgumentNullException(nameof(efFactory));
        }

        public TContext CreateDbContext()
            => _efFactory.CreateDbContext();
    }

}
