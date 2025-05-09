using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Controllers;
using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using MyTts.Data.Repositories;
using MyTts.Repositories;
using MyTts.Routes;
using MyTts.Services;
using MyTts.Storage;
using Polly;
using StackExchange.Redis;
using System.Net.Http.Headers;

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
public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
{
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    var dbAvailable = !string.IsNullOrEmpty(connectionString) && TestSqlConnection(connectionString);

    if (dbAvailable)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
            });
        });

        services.AddScoped(typeof(IRepository<,>), typeof(Repository<,>));
        services.AddScoped<Mp3MetaRepository>();
        //services.AddScoped<NewsRepository>();

        services.AddScoped<IRepository<Mp3Meta, IMp3>>(sp => sp.GetRequiredService<Mp3MetaRepository>());
        //services.AddScoped<IRepository<News, INews>>(sp => sp.GetRequiredService<NewsRepository>());

        // AppDbContextFactory requires AppDbContext, so register it only if available
        services.AddScoped<IAppDbContextFactory, DefaultAppDbContextFactory>();
    }
    else
    {
        // Register dummy DbContext to satisfy dependencies
        services.AddDbContext<AppDbContext>(options => { });

        //services.AddSingleton<IRepository<News, INews>, NullNewsRepository>();

        // In fallback mode, use a dummy factory or skip registration if unused
        services.AddSingleton<IAppDbContextFactory, NullAppDbContextFactory>();
        services.AddSingleton<Mp3MetaRepository, NullMp3MetaRepository>();
    }

    // Prevent AutoMapper from scanning all assemblies (which causes the SqlGuidCaster crash)
    services.AddAutoMapper(cfg => { }, typeof(Program));

    // Application services
    services.AddScoped<Mp3FileRepository>();
    services.AddScoped<IMp3FileRepository>(sp => sp.GetRequiredService<Mp3FileRepository>());

    services.AddScoped<Mp3Service>();
    services.AddScoped<IMp3Service>(sp => sp.GetRequiredService<Mp3Service>());
    services.AddScoped<IAudioConversionService, AudioConversionService>();

    services.AddScoped<NewsFeedsService>();

    services.AddSingleton<Mp3StreamMerger>();

    services.AddTransient<Mp3Controller>();

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
        services.AddScoped<TtsManager>();

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
        bool redisAvailable = false;
        if (redisConfig == null || string.IsNullOrEmpty(redisConfig.ConnectionString))
        {
            services.AddSingleton<IRedisCacheService, NullRedisCacheService>();
            return services;
        }
        try
        {
            var options = ConfigurationOptions.Parse(redisConfig.ConnectionString);
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            options.ConnectTimeout = 5000;

            using (var testConnection = ConnectionMultiplexer.Connect(options))
            {
                redisAvailable = testConnection.IsConnected;
                if (!redisAvailable)
                {
                    services.AddSingleton<IRedisCacheService, NullRedisCacheService>();
                }
                else {
                    services.AddSingleton<IConnectionMultiplexer>(testConnection);
                    services.AddSingleton<IRedisCacheService, RedisCacheService>();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Redis connection failed: {ex.Message}");
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
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("xi-api-key", apiKey);
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