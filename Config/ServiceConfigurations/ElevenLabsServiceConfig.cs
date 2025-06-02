using Microsoft.Extensions.Options;
using MyTts.Services.Clients;
using MyTts.Services.Interfaces;
using MyTts.Services;
using ElevenLabs;
using Polly;
using MyTts.Helpers;

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

        // Register rate limiter
        services.AddSingleton(new CombinedRateLimiter(
            maxConcurrentRequests: 15,
            requestsPerSecond: 15.0
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

            // Apply retry and circuit breaker policies
            var retryPolicy = policyFactory.GetTtsRetryPolicy<Services.VoiceClip>();
            var circuitBreakerPolicy = policyFactory.GetTtsCircuitBreakerPolicy<Services.VoiceClip>();
            var pipeline = new ResiliencePipelineBuilder<Services.VoiceClip>()
                .AddPipeline(retryPolicy)
                .AddPipeline(circuitBreakerPolicy)
                .Build();

            return new ResilientElevenLabsClient(
                client, 
                pipeline, 
                sp.GetRequiredService<ILogger<ResilientElevenLabsClient>>(),
                rateLimiter);
        });
        
        return services;
    }
} 