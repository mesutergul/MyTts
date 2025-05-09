using Microsoft.EntityFrameworkCore;
using MyTts.Data.Context;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Context
{
    public class NullAppDbContextFactory : IAppDbContextFactory
    {
        public AppDbContext Create()
        {
            // Create options for an in-memory database, which doesn't require a real connection.
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: "DummyDatabaseForFallback")
                .Options;

            // Return a new AppDbContext instance with these options.
            // This satisfies the Repository's non-null check but avoids real DB interaction.
            return new AppDbContext(options);
        }
    }
}
