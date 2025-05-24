using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MyTts.Data.Context
{

    // This is ONLY for EF Core design-time operations (migrations, etc.)
    public class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
    {
        public AuthDbContext CreateDbContext(string[] args)
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Get connection string
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            }

            // Configure DbContext options
            var optionsBuilder = new DbContextOptionsBuilder<AuthDbContext>();
            optionsBuilder.UseNpgsql(connectionString);

            return new AuthDbContext(optionsBuilder.Options);
        }
    }
}