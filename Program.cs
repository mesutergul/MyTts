using AutoMapper;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Controllers;
using MyTts.Data;
using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Data.Repositories;
using MyTts.Models;
using MyTts.Repositories;
using MyTts.Routes;
using MyTts.Services;
using MyTts.Storage;
using Polly;
using StackExchange.Redis;
using System.Net.Http.Headers;

FFMpegCore.GlobalFFOptions.Configure(new FFMpegCore.FFOptions
{
    BinaryFolder = @"C:\repos\MyTts-main", // ffmpeg.exe'nin bulundu�u klas�r
    TemporaryFilesFolder = Path.GetTempPath()
});

var builder = WebApplication.CreateBuilder(args);
// Add services to the container
ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
// Configure the HTTP request pipeline
ConfigureMiddleware(app);
ConfigureEndpoints(app);
foreach (var address in app.Urls)
{
    Console.WriteLine($"Application listening on: {address}");
}

app.Run();

static void ConfigureMiddleware(WebApplication app)
{
    // Development specific middleware
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    //app.UseHttpsRedirection();
    app.UseRouting();
    //app.UseCors("AllowAllOrigins");
    app.UseCors("AllowLocalDevelopment");
    app.UseAuthorization();
}

static void ConfigureEndpoints(WebApplication app)
{
    ApiRoutes.RegisterMp3Routes(app);
    app.MapControllers();
}

static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services
        .AddCoreServices(configuration)
        .AddStorageServices(configuration)
        .AddElevenLabsServices(configuration)
        .AddRedisServices(configuration)
        .AddHttpClients()
        .AddApiServices();
}

public static class ServiceCollectionExtensions
{
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

    public static IServiceCollection AddSqlServerDbContext<TContext>(
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
                sqlOptionsAction?.Invoke(sqlOpts); // extra config here
            });

            optionsBuilderAction?.Invoke(options); // e.g., options.EnableDetailedErrors()
        });
    }
    public static IServiceCollection AddInMemoryDbContext<TContext>(
        this IServiceCollection services,
        string dbName)
        where TContext : DbContext
    {
        return services.AddDbContextFactory<TContext>(options =>
        {
            options.UseInMemoryDatabase(dbName);
        });
    }

    public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var dunyaDb = configuration.GetConnectionString("DunyaDb");

        var dbAvailable = !string.IsNullOrEmpty(connectionString) && TestSqlConnection(connectionString);

        if (dbAvailable)
        {
            services.AddSqlServerDbContext<AppDbContext>(connectionString);
            services.AddSqlServerDbContext<DunyaDbContext>(dunyaDb);

            // 2) Register your open-generic adapter:
            services.AddScoped(
                typeof(IGenericDbContextFactory<>),
                typeof(GenericDbContextFactory<>)
            );

            //services.AddScoped(
            //    typeof(IRepository<,>),
            //    typeof(Repository<,,>)
            //);

            //services.AddScoped<IGenericDbContextFactory<AppDbContext>, AppDbContextFactory>();
            //services.AddScoped<IGenericDbContextFactory<DunyaDbContext>, DunyaDbContextFactory>();

            services.AddScoped<Mp3MetaRepository>();
            services.AddScoped<IRepository<Mp3Meta, Mp3Dto>>(sp => sp.GetRequiredService<Mp3MetaRepository>());
            services.AddScoped<NewsRepository>()
                   .AddScoped<IRepository<News, INews>>(sp => sp.GetRequiredService<NewsRepository>());

        }
        else
        {
            // Fallback - no DB
            services.AddInMemoryDbContext<AppDbContext>("InMemoryAppDb");
            services.AddInMemoryDbContext<DunyaDbContext>("InMemoryDunyaDb");
        
            services.AddScoped<IMapper, NullMapper>();
            services.AddScoped<ILogger<NullMp3MetaRepository>, Logger<NullMp3MetaRepository>>();
            services.AddScoped<ILogger<NullNewsRepository>, Logger<NullNewsRepository>>();

            services.AddSingleton(typeof(IGenericDbContextFactory<>), typeof(NullGenericDbContextFactory<>));
            services.AddScoped<Mp3MetaRepository, NullMp3MetaRepository>();
            services.AddScoped<NewsRepository, NullNewsRepository>();
            services.AddScoped<IRepository<Mp3Meta, Mp3Dto>, NullMp3MetaRepository>(sp => sp.GetRequiredService<NullMp3MetaRepository>());
            services.AddScoped<IRepository<News, INews>, NullNewsRepository>(sp => sp.GetRequiredService<NullNewsRepository>());

        }

        // AutoMapper config
        services.AddAutoMapper(cfg => { }, typeof(Program), typeof(HaberMappingProfile), typeof(Mp3MappingProfile));

        // Application services
        services.AddScoped<Mp3Repository>()
                .AddScoped<IMp3Repository>(sp => sp.GetRequiredService<Mp3Repository>());

        services.AddScoped<Mp3Service>()
                .AddScoped<IMp3Service>(sp => sp.GetRequiredService<Mp3Service>());

        services.AddScoped<TtsManagerService>()
                .AddScoped<ITtsManagerService>(sp => sp.GetRequiredService<TtsManagerService>());

        services.AddScoped<Mp3StreamMerger>()
                .AddScoped<IMp3StreamMerger>(sp => sp.GetRequiredService<Mp3StreamMerger>());

        services.AddScoped<NewsFeedsService>();
        services.AddTransient<Mp3Controller>();

        // Logging config
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.AddDebug();
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
            logging.AddFilter("MyTts", LogLevel.Information);
        });

        return services;
    }

    public static IServiceCollection AddStorageServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure storage with proper validation
        services.AddOptions<StorageConfiguration>()
            .Bind(configuration.GetSection("Storage"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register disk configurations 
        services.AddSingleton(sp =>
        {
            var storageConfig = sp.GetRequiredService<IOptions<StorageConfiguration>>().Value;
            var disks = new Dictionary<string, DiskConfiguration>();

            foreach (var disk in storageConfig.Disks)
            {
                disks[disk.Key] = new DiskConfiguration
                {
                    Driver = disk.Value.Driver,
                    Root = disk.Value.Root,
                    Config = disk.Value.Config ?? new DiskConfig()
                };
            }

            return disks;
        });

        // Register TtsManager as a singleton with proper disposal
       // services.AddScoped<TtsManagerService>();

        return services;
    }

    public static IServiceCollection AddElevenLabsServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register ElevenLabs configuration
        services.AddOptions<ElevenLabsConfig>()
            .Bind(configuration.GetSection("ElevenLabs"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // services.AddSingleton<IValidateOptions<ElevenLabsConfig>, ElevenLabsConfig>();

        // Register ElevenLabsClient with better error handling
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ElevenLabsConfig>>().Value;

            // First try configuration, then environment variable
            var apiKey = config.ApiKey ??
                        Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") ??
                        throw new InvalidOperationException("ElevenLabs API key not found");

            return new ElevenLabs.ElevenLabsClient(
                new ElevenLabs.ElevenLabsAuthentication(apiKey),
                new ElevenLabs.ElevenLabsClientSettings("api.elevenlabs.io", "v1"),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ElevenLabsClient")
            );
        });

        return services;
    }

    public static IServiceCollection AddRedisServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register in-memory cache for local caching
        //services.AddMemoryCache(options =>
        //{
        //    options.SizeLimit = 1024; // Set a reasonable size limit
        //});
        var redisConfig = configuration.GetSection("Redis").Get<RedisConfig>();
        if (redisConfig == null || string.IsNullOrEmpty(redisConfig.ConnectionString) || true)
        {
            Console.WriteLine("Redis connection string is missing or empty. Using NullRedisCacheService.");
            services.AddSingleton<IRedisCacheService, NullRedisCacheService>();
            return services;
        }
        try
        {
            var options = ConfigurationOptions.Parse(redisConfig.ConnectionString);
            // Don't need to set these again as they're already in your connection string
            // But keeping them for clarity
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            options.ConnectTimeout = 5000;

            // Register ConnectionMultiplexer as singleton
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var connection = ConnectionMultiplexer.Connect(options);

                // Log connection status
                if (!connection.IsConnected)
                {
                    Console.WriteLine("Warning: Created Redis connection but IsConnected is false");
                }
                else
                {
                    Console.WriteLine("Successfully connected to Redis");
                }

                return connection;
            });

            // Register the actual Redis cache service
            services.AddSingleton<IRedisCacheService, RedisCacheService>();

            Console.WriteLine($"Redis service registered with connection to {redisConfig.ConnectionString}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to configure Redis connection: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            services.AddSingleton<IRedisCacheService, NullRedisCacheService>();
        }

        return services;
    }

    public static IServiceCollection AddHttpClients(this IServiceCollection services)
    {
        // Configure Firebase storage client with resilience
        services.AddHttpClient("FirebaseStorage", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("User-Agent", "MyTts/1.0");
        }).AddTransientHttpErrorPolicy(builder =>
            builder.WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        // Configure ElevenLabs client with proper settings
        services.AddHttpClient("ElevenLabsClient", (sp, client) =>
        {
            var settings = new ElevenLabs.ElevenLabsClientSettings("api.elevenlabs.io", "v1");
            var config = sp.GetRequiredService<IOptions<ElevenLabsConfig>>().Value;

            var apiKey = config.ApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("ElevenLabs API key not found in configuration or environment variables");
            }

            client.BaseAddress = new Uri(settings.BaseRequestUrlFormat.Replace("{0}", ""));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            //  client.DefaultRequestHeaders.Add("xi-api-key", apiKey);
            client.DefaultRequestHeaders.Add("User-Agent", "MyTts");
            client.Timeout = TimeSpan.FromSeconds(60);
        }).AddTransientHttpErrorPolicy(builder =>
            builder.WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        // Configure feed client with resilience
        services.AddHttpClient("FeedClient", client =>
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("User-Agent", "MyTts-FeedClient");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddTransientHttpErrorPolicy(builder =>
            builder.WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        return services;
    }
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddOpenApi();
        services.AddControllers(options =>
        {
            // Add global filters if needed
            options.Filters.Add(new Microsoft.AspNetCore.Mvc.ProducesAttribute("application/json"));
        });

        services.AddCors(options =>
        {
            options.AddPolicy("AllowLocalDevelopment", policy =>
            {
                policy
                    .WithOrigins("http://127.0.0.1:5500", "http://localhost:5500")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders("Content-Disposition") // Important for downloads
                    .AllowCredentials(); // optional if using cookies/auth
            });
        });

        return services;
    }
}