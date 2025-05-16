using Microsoft.Extensions.Options;
using MyTts.Services;
using MyTts.Config;
using StackExchange.Redis;

namespace MyTts.Config.ServiceConfigurations;

public static class RedisServiceConfig
{
    public static IServiceCollection AddRedisServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure Redis options
        services.Configure<MyTts.Config.RedisConfig>(configuration.GetSection("Redis"));

        var redisConfig = configuration.GetSection("Redis").Get<MyTts.Config.RedisConfig>();
        if (redisConfig == null || string.IsNullOrEmpty(redisConfig.ConnectionString))
        {
            Console.WriteLine("Redis connection string is missing or empty. Using NullRedisCacheService.");
            services.AddSingleton<IRedisCacheService>(sp => 
                new NullRedisCacheService(sp.GetRequiredService<ILogger<NullRedisCacheService>>()));
            return services;
        }

        try
        {
            var options = ConfigurationOptions.Parse(redisConfig.ConnectionString);
            options.AbortOnConnectFail = false;
            options.ConnectRetry = redisConfig.MaxRetryAttempts;
            options.ConnectTimeout = redisConfig.ConnectionTimeoutMs;
            options.SyncTimeout = redisConfig.OperationTimeoutMs;
            options.ReconnectRetryPolicy = new ExponentialRetry(redisConfig.RetryDelayMilliseconds);

            // Register ConnectionMultiplexer as singleton
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
                try
                {
                    var connection = ConnectionMultiplexer.Connect(options);
                    if (!connection.IsConnected)
                    {
                        logger.LogWarning("Created Redis connection but IsConnected is false. Falling back to NullRedisCacheService.");
                        return null!;
                    }
                    return connection;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create Redis connection. Falling back to NullRedisCacheService.");
                    return null!;
                }
            });

            // Register the Redis cache service with fallback
            services.AddSingleton<IRedisCacheService>(sp =>
            {
                var connection = sp.GetService<IConnectionMultiplexer>();
                var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
                var nullLogger = sp.GetRequiredService<ILogger<NullRedisCacheService>>();
                var config = sp.GetRequiredService<IOptions<MyTts.Config.RedisConfig>>();

                if (connection == null || !connection.IsConnected)
                {
                    logger.LogWarning("Redis connection not available. Using NullRedisCacheService.");
                    return new NullRedisCacheService(nullLogger);
                }

                try
                {
                    return new RedisCacheService(connection, config, logger);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create RedisCacheService. Using NullRedisCacheService.");
                    return new NullRedisCacheService(nullLogger);
                }
            });

            Console.WriteLine($"Redis service registered with connection to {redisConfig.ConnectionString}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to configure Redis connection: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            services.AddSingleton<IRedisCacheService>(sp => 
                new NullRedisCacheService(sp.GetRequiredService<ILogger<NullRedisCacheService>>()));
        }

        return services;
    }
}

public class RedisConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public int MaxRetryAttempts { get; set; }
    public int ConnectionTimeoutMs { get; set; }
    public int OperationTimeoutMs { get; set; }
    public int RetryDelayMilliseconds { get; set; }
} 