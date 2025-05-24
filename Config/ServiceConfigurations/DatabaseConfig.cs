using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Data.Repositories;
using MyTts.Models;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace MyTts.Config.ServiceConfigurations
{
    public static class DatabaseServiceCollectionExtensions
    {
        // Define an enum to specify the database type
        public enum DatabaseType
        {
            SqlServer,
            PostgreSql,
            InMemory
        }
        public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
        {
            // --- Determine Default Database Type and Connection Strings ---
            var defaultDatabaseTypeString = configuration["Database:Type"];
            Enum.TryParse(defaultDatabaseTypeString, true, out DatabaseType defaultDatabaseType);

            var defaultConnectionString = configuration.GetConnectionString("DefaultConnection");
            var dunyaDbConnectionString = configuration.GetConnectionString("DunyaDb");

            // --- Determine Auth Database Type and Connection String ---
            var authDatabaseTypeString = configuration["Database:Auth:Type"];
            // If auth-specific type is not provided, use the default type
            Enum.TryParse(authDatabaseTypeString ?? defaultDatabaseTypeString, true, out DatabaseType authDatabaseType);

            // Prioritize "Database:Auth:ConnectionString" then "ConnectionStrings:AuthConnection", then fallback to DefaultConnection
            var authConnectionString = configuration["Database:Auth:ConnectionString"]
                                       ?? configuration.GetConnectionString("AuthConnection")
                                       ?? defaultConnectionString;

            // --- Connection Availability Check (Global Switch) ---
            // This check determines if ANY production database connection is available.
            // If false, ALL DbContexts (including AuthDbContext) will fall back to InMemory.
            bool dbAvailable = false;
            if (defaultDatabaseType == DatabaseType.SqlServer)
            {
                dbAvailable = !string.IsNullOrEmpty(defaultConnectionString) && TestSqlConnection(defaultConnectionString);
            }
            else if (defaultDatabaseType == DatabaseType.PostgreSql)
            {
                dbAvailable = !string.IsNullOrEmpty(defaultConnectionString) && TestPgSqlConnection(defaultConnectionString);
            }
            // If DatabaseType is InMemory, dbAvailable will remain false, leading to ConfigureInMemoryDatabase
            // If Database:Type is not configured, it will default to SqlServer by Enum.TryParse and then attempt to test.

            if (dbAvailable)
            {
                ConfigureProductionDatabase(
                    services,
                    defaultDatabaseType,
                    defaultConnectionString!,
                    dunyaDbConnectionString!,
                    authDatabaseType,          // Pass auth-specific type
                    authConnectionString!      // Pass auth-specific connection string
                );
            }
            else
            {
                // Fallback to in-memory for all DbContexts if production DBs are not available
                ConfigureInMemoryDatabase(services);
            }

            return services;
        }

        private static void ConfigureProductionDatabase(
            IServiceCollection services,
            DatabaseType defaultDatabaseType,
            string defaultConnectionString,
            string dunyaDbConnectionString,
            DatabaseType authDatabaseType, // New parameter for AuthDbContext type
            string authConnectionString    // New parameter for AuthDbContext connection string
        )
        {
            // Configure AppDbContext and DunyaDbContext based on the default database type
            switch (defaultDatabaseType)
            {
                case DatabaseType.SqlServer:
                    services.AddSqlServerDbContext<AppDbContext>(defaultConnectionString);
                    services.AddSqlServerDbContext<DunyaDbContext>(dunyaDbConnectionString);
                    break;
                case DatabaseType.PostgreSql:
                    services.AddPostgreSqlDbContext<AppDbContext>(defaultConnectionString);
                    services.AddPostgreSqlDbContext<DunyaDbContext>(dunyaDbConnectionString);
                    break;
                case DatabaseType.InMemory: // This case should theoretically not be hit if dbAvailable is true
                    throw new InvalidOperationException("InMemory is not supported for production configuration. Check your 'Database:Type' setting.");
                default:
                    throw new InvalidOperationException($"Unsupported default production database type: {defaultDatabaseType}");
            }

            // Configure AuthDbContext based on its specific database type
            switch (authDatabaseType)
            {
                case DatabaseType.SqlServer:
                    services.AddSqlServerDbContext<AuthDbContext>(authConnectionString);
                    break;
                case DatabaseType.PostgreSql:
                    services.AddPostgreSqlDbContext<AuthDbContext>(authConnectionString);
                    break;
                case DatabaseType.InMemory:
                    // This scenario means the main connection was available, but Auth wants In-Memory.
                    // This is an unusual mix, but we can support it if intended.
                    // Otherwise, you might want to throw an error here if you strictly enforce
                    // "all prod or all in-memory".
                    services.AddInMemoryDbContext<AuthDbContext>("InMemoryAuthDbMixed"); // Use a distinct name
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported Auth production database type: {authDatabaseType}");
            }

            // Register the DbContext factory first
            services.AddScoped(
                typeof(IGenericDbContextFactory<>),
                typeof(GenericDbContextFactory<>)
            );

            // Register repositories with proper order
            services.AddScoped<Mp3MetaRepository>()
                   .AddScoped<IMp3MetaRepository>(sp => sp.GetRequiredService<Mp3MetaRepository>())
                   .AddScoped<IRepository<Mp3Meta, Mp3Dto>>(sp => sp.GetRequiredService<Mp3MetaRepository>());

            services.AddScoped<NewsRepository>()
                   .AddScoped<INewsRepository>(sp => sp.GetRequiredService<NewsRepository>())
                   .AddScoped<IRepository<News, NewsDto>>(sp => sp.GetRequiredService<NewsRepository>());
        }

        private static void ConfigureInMemoryDatabase(IServiceCollection services)
        {
            services.AddInMemoryDbContext<AppDbContext>("InMemoryAppDb");
            services.AddInMemoryDbContext<DunyaDbContext>("InMemoryDunyaDb");
            services.AddInMemoryDbContext<AuthDbContext>("InMemoryAuthDb"); // Add AuthDbContext

            // Assuming these are "null" or "mock" repositories for in-memory testing/development
            services.AddScoped<ILogger<NullMp3MetaRepository>, Logger<NullMp3MetaRepository>>();
            services.AddScoped<ILogger<NullNewsRepository>, Logger<NullNewsRepository>>();

            services.AddSingleton(typeof(IGenericDbContextFactory<>), typeof(NullGenericDbContextFactory<>));

            // Register null repositories with proper order
            services.AddScoped<NullMp3MetaRepository>()
                   .AddScoped<IMp3MetaRepository>(sp => sp.GetRequiredService<NullMp3MetaRepository>())
                   .AddScoped<IRepository<Mp3Meta, Mp3Dto>>(sp => sp.GetRequiredService<NullMp3MetaRepository>());

            services.AddScoped<NullNewsRepository>()
                   .AddScoped<INewsRepository>(sp => sp.GetRequiredService<NullNewsRepository>())
                   .AddScoped<IRepository<News, NewsDto>>(sp => sp.GetRequiredService<NullNewsRepository>());
        }

        /// <summary>
        /// Tests a SQL Server database connection.
        /// </summary>
        private static bool TestSqlConnection(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return false;
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tests a PostgreSQL database connection.
        /// </summary>
        private static bool TestPgSqlConnection(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return false;
            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IServiceCollection AddSqlServerDbContext<TContext>(
            this IServiceCollection services,
            string connectionString,
            Action<SqlServerDbContextOptionsBuilder>? sqlOptionsAction = null,
            Action<DbContextOptionsBuilder>? optionsBuilderAction = null)
            where TContext : DbContext
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString), $"Connection string for {typeof(TContext).Name} cannot be null or empty for SQL Server.");
            }
            return services.AddDbContextFactory<TContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOpts =>
                {
                    sqlOpts.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlOpts.CommandTimeout(30);
                    sqlOptionsAction?.Invoke(sqlOpts);
                });

                options.ConfigureWarnings(warnings =>
                {
                    warnings.Ignore(RelationalEventId.MultipleCollectionIncludeWarning);
                    warnings.Ignore(SqlServerEventId.SavepointsDisabledBecauseOfMARS);
                });

                optionsBuilderAction?.Invoke(options);
            });
        }

        /// <summary>
        /// Adds a PostgreSQL DbContext to the service collection.
        /// </summary>
        private static IServiceCollection AddPostgreSqlDbContext<TContext>(
            this IServiceCollection services,
            string connectionString,
            Action<NpgsqlDbContextOptionsBuilder>? pgOptionsAction = null,
            Action<DbContextOptionsBuilder>? optionsBuilderAction = null)
            where TContext : DbContext
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString), $"Connection string for {typeof(TContext).Name} cannot be null or empty for PostgreSQL.");
            }
            return services.AddDbContextFactory<TContext>(options =>
            {
                options.UseNpgsql(connectionString, pgOpts =>
                {
                    pgOpts.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);
                    pgOpts.CommandTimeout(30);
                    pgOptionsAction?.Invoke(pgOpts);
                });

                optionsBuilderAction?.Invoke(options);
            });
        }

        private static IServiceCollection AddInMemoryDbContext<TContext>(
            this IServiceCollection services,
            string dbName)
            where TContext : DbContext
        {
            return services.AddDbContextFactory<TContext>(options =>
            {
                options.UseInMemoryDatabase(dbName);
            });
        }

    }
}

/* public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Determine the database type from configuration, default to SQL Server if not specified
            var databaseTypeString = configuration["Database:Type"];
            Enum.TryParse(databaseTypeString, true, out DatabaseType databaseType);

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var dunyaDb = configuration.GetConnectionString("DunyaDb");

            bool dbAvailable = false;

            // Test connection based on the chosen database type
            if (databaseType == DatabaseType.SqlServer)
            {
                dbAvailable = !string.IsNullOrEmpty(connectionString) && TestSqlConnection(connectionString);
            }
            else if (databaseType == DatabaseType.PostgreSql)
            {
                dbAvailable = !string.IsNullOrEmpty(connectionString) && TestPgSqlConnection(connectionString);
            }
            // No connection test needed for InMemory

            if (dbAvailable)
            {
                ConfigureProductionDatabase(services, databaseType, connectionString!, dunyaDb!);
            }
            else
            {
                ConfigureInMemoryDatabase(services);
            }

            return services;
        }

        private static void ConfigureProductionDatabase(IServiceCollection services, DatabaseType databaseType, string connectionString, string dunyaDb)
        {
            switch (databaseType)
            {
                case DatabaseType.SqlServer:
                    services.AddSqlServerDbContext<AppDbContext>(connectionString);
                    services.AddSqlServerDbContext<DunyaDbContext>(dunyaDb);
                    services.AddSqlServerDbContext<AuthDbContext>(connectionString); // Add AuthDbContext
                    break;
                case DatabaseType.PostgreSql:
                    services.AddPostgreSqlDbContext<AppDbContext>(connectionString);
                    services.AddPostgreSqlDbContext<DunyaDbContext>(dunyaDb);
                    services.AddPostgreSqlDbContext<AuthDbContext>(connectionString); // Add AuthDbContext
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported production database type: {databaseType}");
            }

            // Register the DbContext factory first
            services.AddScoped(
                typeof(IGenericDbContextFactory<>),
                typeof(GenericDbContextFactory<>)
            );

            // Register repositories with proper order
            services.AddScoped<Mp3MetaRepository>()
                   .AddScoped<IMp3MetaRepository>(sp => sp.GetRequiredService<Mp3MetaRepository>())
                   .AddScoped<IRepository<Mp3Meta, Mp3Dto>>(sp => sp.GetRequiredService<Mp3MetaRepository>());

            services.AddScoped<NewsRepository>()
                   .AddScoped<INewsRepository>(sp => sp.GetRequiredService<NewsRepository>())
                   .AddScoped<IRepository<News, NewsDto>>(sp => sp.GetRequiredService<NewsRepository>());
        }

        private static void ConfigureInMemoryDatabase(IServiceCollection services)
        {
            services.AddInMemoryDbContext<AppDbContext>("InMemoryAppDb");
            services.AddInMemoryDbContext<DunyaDbContext>("InMemoryDunyaDb");
            services.AddInMemoryDbContext<AuthDbContext>("InMemoryAuthDb"); // Add AuthDbContext

            services.AddScoped<ILogger<NullMp3MetaRepository>, Logger<NullMp3MetaRepository>>();
            services.AddScoped<ILogger<NullNewsRepository>, Logger<NullNewsRepository>>();

            services.AddSingleton(typeof(IGenericDbContextFactory<>), typeof(NullGenericDbContextFactory<>));

            // Register null repositories with proper order
            services.AddScoped<NullMp3MetaRepository>()
                   .AddScoped<IMp3MetaRepository>(sp => sp.GetRequiredService<NullMp3MetaRepository>())
                   .AddScoped<IRepository<Mp3Meta, Mp3Dto>>(sp => sp.GetRequiredService<NullMp3MetaRepository>());

            services.AddScoped<NullNewsRepository>()
                   .AddScoped<INewsRepository>(sp => sp.GetRequiredService<NullNewsRepository>())
                   .AddScoped<IRepository<News, NewsDto>>(sp => sp.GetRequiredService<NullNewsRepository>());
        }

        /// <summary>
        /// Tests a SQL Server database connection.
        /// </summary>
        private static bool TestSqlConnection(string connectionString)
        {
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tests a PostgreSQL database connection.
        /// </summary>
        private static bool TestPgSqlConnection(string connectionString)
        {
            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IServiceCollection AddSqlServerDbContext<TContext>(
            this IServiceCollection services,
            string connectionString,
            Action<SqlServerDbContextOptionsBuilder>? sqlOptionsAction = null,
            Action<DbContextOptionsBuilder>? optionsBuilderAction = null)
            where TContext : DbContext
        {
            return services.AddDbContextFactory<TContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOpts =>
                {
                    sqlOpts.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlOpts.CommandTimeout(30);
                    // MARS is enabled by default in EF Core for SQL Server
                    sqlOptionsAction?.Invoke(sqlOpts);
                });

                // Configure warnings to ignore MARS-related warnings (specific to SQL Server)
                options.ConfigureWarnings(warnings =>
                {
                    warnings.Ignore(RelationalEventId.MultipleCollectionIncludeWarning);
                    warnings.Ignore(SqlServerEventId.SavepointsDisabledBecauseOfMARS);
                });

                optionsBuilderAction?.Invoke(options);
            });
        }

        /// <summary>
        /// Adds a PostgreSQL DbContext to the service collection.
        /// </summary>
        private static IServiceCollection AddPostgreSqlDbContext<TContext>(
            this IServiceCollection services,
            string connectionString,
            Action<NpgsqlDbContextOptionsBuilder>? pgOptionsAction = null,
            Action<DbContextOptionsBuilder>? optionsBuilderAction = null)
            where TContext : DbContext
        {
            return services.AddDbContextFactory<TContext>(options =>
            {
                options.UseNpgsql(connectionString, pgOpts =>
                {
                    pgOpts.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);
                    pgOpts.CommandTimeout(30);
                    pgOptionsAction?.Invoke(pgOpts);
                });

                optionsBuilderAction?.Invoke(options);
            });
        }

        private static IServiceCollection AddInMemoryDbContext<TContext>(
            this IServiceCollection services,
            string dbName)
            where TContext : DbContext
        {
            return services.AddDbContextFactory<TContext>(options =>
            {
                options.UseInMemoryDatabase(dbName);
            });
        }*/