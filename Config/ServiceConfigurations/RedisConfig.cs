using Microsoft.Extensions.Options;
using MyTts.Services;
using StackExchange.Redis;
using MyTts.Services.Interfaces;
using Polly;
using Polly.Retry;

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

        // Configure retry policy for Redis operations
        services.AddSingleton<AsyncRetryPolicy>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
            return Policy
                .Handle<RedisConnectionException>()
                .Or<RedisTimeoutException>()
                .WaitAndRetryAsync(
                    redisConfig.MaxRetryAttempts,
                    retryAttempt => TimeSpan.FromMilliseconds(redisConfig.RetryDelayMilliseconds * Math.Pow(2, retryAttempt - 1)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        logger.LogWarning(exception,
                            "Retry {RetryCount} after {Delay}ms for Redis operation {OperationKey}",
                            retryCount, timeSpan.TotalMilliseconds, context.OperationKey);
                    });
        });

        try
        {
            var options = ConfigurationOptions.Parse(redisConfig.ConnectionString);
            options.AbortOnConnectFail = false;
            options.ConnectRetry = redisConfig.MaxRetryAttempts;
            options.ConnectTimeout = redisConfig.ConnectionTimeoutMs;
            options.SyncTimeout = redisConfig.OperationTimeoutMs;
            options.ReconnectRetryPolicy = new ExponentialRetry(redisConfig.RetryDelayMilliseconds);
            options.KeepAlive = 60; // Keep-alive every 60 seconds
            options.ConnectTimeout = 5000; // 5 seconds connection timeout
            options.SyncTimeout = 5000; // 5 seconds sync timeout

            // Register ConnectionMultiplexer as singleton with retry policy
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
                var retryPolicy = sp.GetRequiredService<AsyncRetryPolicy>();

                try
                {
                    var connection = ConnectionMultiplexer.Connect(options);
                    if (!connection.IsConnected)
                    {
                        logger.LogWarning("Created Redis connection but IsConnected is false. Falling back to NullRedisCacheService.");
                        return null!;
                    }

                    // Subscribe to connection events
                    connection.ConnectionFailed += (sender, e) =>
                    {
                        logger.LogWarning(e.Exception, "Redis connection failed: {FailureType}", e.FailureType);
                    };

                    connection.ConnectionRestored += (sender, e) =>
                    {
                        logger.LogInformation("Redis connection restored");
                    };

                    connection.ErrorMessage += (sender, e) =>
                    {
                        logger.LogError("Redis error: {Message}", e.Message);
                    };

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
                var retryPolicy = sp.GetRequiredService<AsyncRetryPolicy>();

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