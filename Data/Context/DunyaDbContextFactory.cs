using Microsoft.EntityFrameworkCore;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Context
{
    public class DunyaDbContextFactory : IGenericDbContextFactory<DunyaDbContext>
    {
        private readonly IDbContextFactory<DunyaDbContext> _factory;
        public DunyaDbContextFactory(IDbContextFactory<DunyaDbContext> factory) => _factory = factory;
        public DunyaDbContext CreateDbContext() => _factory.CreateDbContext();
    }
}
