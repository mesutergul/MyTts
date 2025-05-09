// File: MyTts.Services.Interfaces/ICacheService.cs

using System;
using System.Threading.Tasks;

namespace MyTts.Services.Interfaces
{
    public interface ICacheService
    {
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
        Task RemoveAsync(string key);
        Task<bool> ExistsAsync(string key);
    }
}
// File: MyTts.Services/RedisCacheService.cs

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json; // Assuming you use JSON for serialization
using StackExchange.Redis;
using MyTts.Services.Interfaces; // Important: Add this using directive

namespace MyTts.Services
{
    public class RedisCacheService : ICacheService
    {
        private readonly IDatabase _db;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly ConnectionMultiplexer _redisMultiplexer; // Store for IsConnected check

        // Constructor now takes ConnectionMultiplexer, not IConfiguration
        public RedisCacheService(ConnectionMultiplexer redisMultiplexer, ILogger<RedisCacheService> logger)
        {
            _logger = logger;
            _redisMultiplexer = redisMultiplexer;

            if (redisMultiplexer == null || !redisMultiplexer.IsConnected)
            {
                _logger.LogError("Redis ConnectionMultiplexer is not connected. RedisCacheService cannot operate.");
                // Throwing here is okay, as this service should only be instantiated
                // when the ConnectionMultiplexer is ready. The DI container will then
                // use the fallback.
                throw new InvalidOperationException("Redis ConnectionMultiplexer is not connected.");
            }
            _db = redisMultiplexer.GetDatabase();
            _logger.LogInformation("RedisCacheService initialized with connected Redis instance.");
        }

        public async Task<T> GetAsync<T>(string key)
        {
            if (_db == null || !_redisMultiplexer.IsConnected) // Defensive check, though DI should prevent this
            {
                _logger.LogWarning("Redis connection not available for GET operation. Key: {Key}", key);
                return default(T);
            }
            try
            {
                var value = await _db.StringGetAsync(key);
                if (value.IsNullOrEmpty)
                {
                    _logger.LogDebug("Cache miss for key: {Key}", key);
                    return default(T);
                }
                _logger.LogDebug("Cache hit for key: {Key}", key);
                return JsonConvert.DeserializeObject<T>(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting value for key: {Key} from Redis.", key);
                // Rethrow if you want the caller to handle, or return default(T) for a soft failure
                throw;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            if (_db == null || !_redisMultiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for SET operation. Key: {Key}", key);
                return;
            }
            try
            {
                var serializedValue = JsonConvert.SerializeObject(value);
                await _db.StringSetAsync(key, serializedValue, expiry);
                _logger.LogDebug("Set value for key: {Key} in Redis with expiry: {Expiry}", key, expiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis SET failed for key: {Key}.", key);
                throw;
            }
        }

        public async Task RemoveAsync(string key)
        {
            if (_db == null || !_redisMultiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for REMOVE operation. Key: {Key}", key);
                return;
            }
            try
            {
                await _db.KeyDeleteAsync(key);
                _logger.LogDebug("Removed key: {Key} from Redis.", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis REMOVE failed for key: {Key}.", key);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            if (_db == null || !_redisMultiplexer.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for EXISTS operation. Key: {Key}", key);
                return false;
            }
            try
            {
                return await _db.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis EXISTS failed for key: {Key}.", key);
                throw;
            }
        }
    }
}
// File: MyTts.Services/InMemoryCacheService.cs

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json; // For consistency with serialization
using MyTts.Services.Interfaces; // Important: Add this using directive

namespace MyTts.Services
{
    public class InMemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<InMemoryCacheService> _logger;

        public InMemoryCacheService(IMemoryCache cache, ILogger<InMemoryCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
            _logger.LogInformation("Using In-Memory Cache Service as fallback.");
        }

        public Task<T> GetAsync<T>(string key)
        {
            _logger.LogDebug("In-Memory Cache: Getting value for key: {Key}", key);
            if (_cache.TryGetValue(key, out string serializedValue))
            {
                return Task.FromResult(JsonConvert.DeserializeObject<T>(serializedValue));
            }
            return Task.FromResult(default(T));
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            _logger.LogDebug("In-Memory Cache: Setting value for key: {Key} with expiry: {Expiry}", key, expiry);
            var serializedValue = JsonConvert.SerializeObject(value);
            var cacheEntryOptions = new MemoryCacheEntryOptions();
            if (expiry.HasValue)
            {
                cacheEntryOptions.SetAbsoluteExpiration(expiry.Value);
            }
            _cache.Set(key, serializedValue, cacheEntryOptions);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            _logger.LogDebug("In-Memory Cache: Removing value for key: {Key}", key);
            _cache.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key)
        {
            _logger.LogDebug("In-Memory Cache: Checking existence for key: {Key}", key);
            return Task.FromResult(_cache.TryGetValue(key, out _));
        }
    }
}
// File: Program.cs or a dedicated ServiceCollectionExtensions file

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis; // Make sure this is present
using MyTts.Services.Interfaces; // Add this
using MyTts.Services; // Add this

public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration)
{
    // ... existing database logic ...

    // --- Redis/Cache Service Configuration ---
    var redisConnectionString = configuration.GetConnectionString("RedisConnection");
    bool redisAvailable = false;

    if (!string.IsNullOrEmpty(redisConnectionString))
    {
        try
        {
            // Try to establish a connection to Redis. Use a short connect timeout for the health check.
            var connectionOptions = ConfigurationOptions.Parse(redisConnectionString);
            connectionOptions.ConnectTimeout = 5000; // 5 seconds for connection attempt
            connectionOptions.SyncTimeout = 1000; // 1 second for synchronous operations during test
            connectionOptions.AbortOnConnectFail = false; // Don't throw immediately

            using (var testConnection = ConnectionMultiplexer.Connect(connectionOptions))
            {
                redisAvailable = testConnection.IsConnected;
                if (!redisAvailable)
                {
                    Console.WriteLine("Redis connection test failed: ConnectionMultiplexer reported not connected.");
                    // Log details of the failure if needed. TestConnection will internally log errors.
                }
                else
                {
                    Console.WriteLine("Successfully connected to Redis during health check.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to Redis during health check: {ex.Message}");
            // Logging here using ILogger is tricky if ILogger is not yet fully built/available
            // For application startup, Console.WriteLine is often used for such critical status.
            // Once ILogger is built, subsequent uses can log properly.
        }
    }
    else
    {
        Console.WriteLine("RedisConnection string is not configured. Redis will not be used.");
    }

    if (redisAvailable)
    {
        Console.WriteLine("Redis is available. Registering RedisCacheService.");
        // Register the ConnectionMultiplexer as a Singleton
        services.AddSingleton<ConnectionMultiplexer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ConnectionMultiplexer>>();
            try
            {
                var multiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
                // Log connection events for better monitoring
                multiplexer.ConnectionFailed += (sender, e) => logger.LogError(e.Exception, "Redis connection failed: {FailureType}", e.FailureType);
                multiplexer.ConnectionRestored += (sender, e) => logger.LogInformation("Redis connection restored: {FailureType}", e.FailureType);
                multiplexer.ErrorMessage += (sender, e) => logger.LogError("Redis error message: {Message}", e.Message);
                multiplexer.InternalError += (sender, e) => logger.LogError(e.Exception, "Redis internal error: {Origin}", e.Origin);

                logger.LogInformation("Successfully established ConnectionMultiplexer for application use.");
                return multiplexer;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Critical: Failed to establish main ConnectionMultiplexer. This should not happen if health check passed.");
                // If it fails here despite the health check, it's a serious problem.
                throw;
            }
        });
        // Register RedisCacheService as the implementation for ICacheService
        services.AddScoped<ICacheService, RedisCacheService>();
    }
    else
    {
        Console.WriteLine("Redis is NOT available. Registering InMemoryCacheService as fallback.");
        // Add the built-in IMemoryCache service first
        services.AddMemoryCache();
        // Register InMemoryCacheService as the implementation for ICacheService
        services.AddScoped<ICacheService, InMemoryCacheService>();
    }

    // ... existing AutoMapper, other services, and logging configuration ...
    services.AddAutoMapper(cfg => { /* your mappings */ }, typeof(Program)); // Example, ensure your mappings are defined

    // Other application services (these should now consume ICacheService)
    services.AddScoped<Mp3FileRepository>();
    services.AddScoped<IMp3FileRepository>(sp => sp.GetRequiredService<Mp3FileRepository>());
    services.AddScoped<Mp3Service>();
    services.AddScoped<IMp3Service>(sp => sp.GetRequiredService<Mp3Service>());
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
// Example: NewsFeedsService (adjust to your actual service names)

using MyTts.Data.Entities; // Assuming News entity is here
using MyTts.Data.Repositories; // Assuming NewsRepository is here
using MyTts.Services.Interfaces; // Important: Add this using directive

namespace MyTts.Services
{
    public class NewsFeedsService
    {
        private readonly ICacheService _cacheService;
        private readonly NewsRepository _newsRepository; // Assuming you still need this

        public NewsFeedsService(ICacheService cacheService, NewsRepository newsRepository)
        {
            _cacheService = cacheService;
            _newsRepository = newsRepository;
        }

        public async Task<IEnumerable<News>> GetLatestNewsAsync(string category)
        {
            string cacheKey = $"news:{category}";
            var cachedNews = await _cacheService.GetAsync<IEnumerable<News>>(cacheKey);

            if (cachedNews != null)
            {
                return cachedNews;
            }

            // If not in cache, fetch from the actual data source (e.g., database)
            var newsFromDb = await _newsRepository.GetNewsByCategoryAsync(category);

            // Cache the retrieved data for a certain period (e.g., 30 minutes)
            await _cacheService.SetAsync(cacheKey, newsFromDb, TimeSpan.FromMinutes(30));

            return newsFromDb;
        }
        // ... other methods
    }
}
/*
Microsoft.Extensions.Caching.Memory (for IMemoryCache)
StackExchange.Redis (for Redis operations)
Newtonsoft.Json (if you are serializing objects to JSON for caching)
*/