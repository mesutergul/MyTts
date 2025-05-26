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
        
        // Register MP3 stream merger
        services.AddScoped<IMp3StreamMerger, Mp3StreamMerger>();

        return services;
    }
} 