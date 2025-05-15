using Microsoft.EntityFrameworkCore;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Context
{
    public class AppDbContextFactory : IGenericDbContextFactory<AppDbContext>
    {
        private readonly IDbContextFactory<AppDbContext> _efFactory;

        public AppDbContextFactory(IDbContextFactory<AppDbContext> efFactory)
        {
            _efFactory = efFactory;
        }

        public AppDbContext CreateDbContext() => _efFactory.CreateDbContext();
    }
}
