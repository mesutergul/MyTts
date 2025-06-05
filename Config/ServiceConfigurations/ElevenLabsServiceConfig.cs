using Microsoft.Extensions.Options;
using MyTts.Services.Clients;
using MyTts.Services.Interfaces;
using MyTts.Services;
using ElevenLabs;
using Polly;
using MyTts.Helpers;
using ElevenLabs.Voices;

namespace MyTts.Config.ServiceConfigurations;

public static class ElevenLabsServiceConfig
{
    private const string Mp3StreamMergerRateLimiterKey = "Mp3StreamMergerRateLimiter";

    public static IServiceCollection AddElevenLabsServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure ElevenLabs options with validation
        services.AddOptions<ElevenLabsConfig>()
            .Bind(configuration.GetSection("ElevenLabs"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register shared rate limiter for ElevenLabs API
        services.AddSingleton<CombinedRateLimiter>(sp => new CombinedRateLimiter(
            maxConcurrentRequests: 15,
            requestsPerSecond: 15.0,
            queueSize: 20,  // Allow up to 20 requests to be queued
            logger: sp.GetRequiredService<ILogger<CombinedRateLimiter>>()
        ));

        // Register rate limiter factory for Mp3StreamMerger
        services.AddKeyedSingleton<Func<CombinedRateLimiter>>(Mp3StreamMergerRateLimiterKey, (sp, key) => () => new CombinedRateLimiter(
            maxConcurrentRequests: 2,  // Match MaxConcurrentMerges
            requestsPerSecond: 5.0,    // Lower rate for file operations
            queueSize: 5,              // Smaller queue for file operations
            logger: sp.GetRequiredService<ILogger<CombinedRateLimiter>>()
        ));

        // Register ElevenLabs client with proper initialization
        services.AddScoped(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ElevenLabsConfig>>().Value;
            var logger = sp.GetRequiredService<ILogger<ElevenLabsClient>>();
            var policyFactory = sp.GetRequiredService<SharedPolicyFactory>();
            var rateLimiter = sp.GetRequiredService<CombinedRateLimiter>();
            
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
            var client = new ElevenLabsClient(auth, settings);

            // Create separate pipelines for Voice and VoiceClip
            var voicePipeline = policyFactory.CreateTtsPipeline<Voice>();
            var voiceClipPipeline = policyFactory.CreateTtsPipeline<ElevenLabs.VoiceClip>();

            return new ResilientElevenLabsClient(
                client, 
                voiceClipPipeline,
                voicePipeline,
                sp.GetRequiredService<ILogger<ResilientElevenLabsClient>>(),
                rateLimiter);
        });

        // Register Mp3StreamMerger with its specific rate limiter
        services.AddScoped<IMp3StreamMerger>(sp => new Mp3StreamMerger(
            sp.GetRequiredService<ILogger<Mp3StreamMerger>>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredKeyedService<Func<CombinedRateLimiter>>(Mp3StreamMergerRateLimiterKey)(),
            sp.GetRequiredService<SharedPolicyFactory>()
        ));

        return services;
    }
} 