using Microsoft.Extensions.Options;
using MyTts.Services.Clients;
using MyTts.Services.Interfaces;
using MyTts.Services;
using ElevenLabs;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace MyTts.Config.ServiceConfigurations;

public static class ElevenLabsServiceConfig
{
    public static IServiceCollection AddElevenLabsServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure ElevenLabs options with validation
        services.AddOptions<ElevenLabsConfig>()
            .Bind(configuration.GetSection("ElevenLabs"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Configure resilience policies
        services.AddSingleton<AsyncRetryPolicy>(sp =>
        {
            return Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        var logger = sp.GetRequiredService<ILogger<ElevenLabsClient>>();
                        logger.LogWarning(exception, 
                            "Retry {RetryCount} after {Delay}ms for operation {OperationKey}", 
                            retryCount, timeSpan.TotalMilliseconds, context.OperationKey);
                    });
        });

        services.AddSingleton<AsyncCircuitBreakerPolicy>(sp =>
        {
            return Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 2,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (exception, duration) =>
                    {
                        var logger = sp.GetRequiredService<ILogger<ElevenLabsClient>>();
                        logger.LogWarning(exception,
                            "Circuit breaker opened for {Duration} seconds due to {ExceptionType}",
                            duration.TotalSeconds, exception.GetType().Name);
                    },
                    onReset: () =>
                    {
                        var logger = sp.GetRequiredService<ILogger<ElevenLabsClient>>();
                        logger.LogInformation("Circuit breaker reset - service is healthy again");
                    },
                    onHalfOpen: () =>
                    {
                        var logger = sp.GetRequiredService<ILogger<ElevenLabsClient>>();
                        logger.LogInformation("Circuit breaker half-open - testing service health");
                    });
        });

        // Register ElevenLabs client with proper initialization
        services.AddScoped(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ElevenLabsConfig>>().Value;
            var logger = sp.GetRequiredService<ILogger<ElevenLabsClient>>();
            
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                var envKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
                if (string.IsNullOrEmpty(envKey))
                {
                    logger.LogError("ElevenLabs API key not found in configuration or environment variables");
                    throw new InvalidOperationException("ElevenLabs API key not found in configuration or environment variables");
                }
                config.ApiKey = envKey;
            }

            var settings = new ElevenLabsClientSettings("api.elevenlabs.io", "v1");
            var auth = new ElevenLabsAuthentication(config.ApiKey);
            return new ElevenLabsClient(auth, settings);
        });
        
        // Register TTS client
        services.AddScoped<TtsClient>();
        
        // Register MP3 stream merger
        services.AddScoped<IMp3StreamMerger, Mp3StreamMerger>();

        return services;
    }
} 