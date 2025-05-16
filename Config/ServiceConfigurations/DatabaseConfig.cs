using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AutoMapper;
using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Data.Repositories;
using MyTts.Models;
using MyTts.Data;

namespace MyTts.Config.ServiceConfigurations;

public static class DatabaseConfig
{
    public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var dunyaDb = configuration.GetConnectionString("DunyaDb");

        var dbAvailable = !string.IsNullOrEmpty(connectionString) && TestSqlConnection(connectionString);

        if (dbAvailable)
        {
            ConfigureProductionDatabase(services, connectionString!, dunyaDb!);
        }
        else
        {
            ConfigureInMemoryDatabase(services);
        }

        return services;
    }

    private static void ConfigureProductionDatabase(IServiceCollection services, string connectionString, string dunyaDb)
    {
        services.AddSqlServerDbContext<AppDbContext>(connectionString);
        services.AddSqlServerDbContext<DunyaDbContext>(dunyaDb);

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
               .AddScoped<IRepository<News, INews>>(sp => sp.GetRequiredService<NewsRepository>());
    }

    private static void ConfigureInMemoryDatabase(IServiceCollection services)
    {
        services.AddInMemoryDbContext<AppDbContext>("InMemoryAppDb");
        services.AddInMemoryDbContext<DunyaDbContext>("InMemoryDunyaDb");

        services.AddScoped<ILogger<NullMp3MetaRepository>, Logger<NullMp3MetaRepository>>();
        services.AddScoped<ILogger<NullNewsRepository>, Logger<NullNewsRepository>>();

        services.AddSingleton(typeof(IGenericDbContextFactory<>), typeof(NullGenericDbContextFactory<>));

        // Register null repositories with proper order
        services.AddScoped<NullMp3MetaRepository>()
               .AddScoped<IMp3MetaRepository>(sp => sp.GetRequiredService<NullMp3MetaRepository>())
               .AddScoped<IRepository<Mp3Meta, Mp3Dto>>(sp => sp.GetRequiredService<NullMp3MetaRepository>());

        services.AddScoped<NullNewsRepository>()
               .AddScoped<INewsRepository>(sp => sp.GetRequiredService<NullNewsRepository>())
               .AddScoped<IRepository<News, INews>>(sp => sp.GetRequiredService<NullNewsRepository>());
    }

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
                sqlOpts.EnableRetryOnFailure(3);
                sqlOpts.CommandTimeout(30);
                sqlOptionsAction?.Invoke(sqlOpts);
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