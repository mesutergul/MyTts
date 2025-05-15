using Microsoft.EntityFrameworkCore;

namespace MyTts.Data.Interfaces
{
    public interface IGenericDbContextFactory<TContext>
    where TContext : DbContext
    {
        TContext CreateDbContext();
    }
}
