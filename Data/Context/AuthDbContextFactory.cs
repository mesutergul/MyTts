
using Microsoft.EntityFrameworkCore;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Context
{
    public class AuthDbContextFactory : IGenericDbContextFactory<AuthDbContext>
    {
        private readonly IDbContextFactory<AuthDbContext> _efFactory;

        public AuthDbContextFactory(IDbContextFactory<AuthDbContext> efFactory)
        {
            _efFactory = efFactory;
        }

        public AuthDbContext CreateDbContext() => _efFactory.CreateDbContext();
    }

}