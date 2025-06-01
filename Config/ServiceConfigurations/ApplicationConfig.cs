using MyTts.Repositories;
using MyTts.Services;
using MyTts.Services.Interfaces;
using MyTts.Services.Clients;
using Polly; // Keep this for ResiliencePipeline
namespace MyTts.Config.ServiceConfigurations;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register repositories
        services.AddScoped<IMp3Repository, Mp3Repository>();
        
        // Register application services
        services.AddScoped<IMp3Service, Mp3Service>();
        services.AddScoped<ITtsClient, TtsClient>();
        services.AddSingleton<SharedPolicyFactory>();

        // Configure and register notification service with resilience policies
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.AddHttpClient<INotificationService, NotificationService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "MyTts-NotificationService");
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddResilienceHandler("NotificationServiceCombinedPolicy", (pipelineBuilder, serviceProvider ) =>
        {
            var policyFactory = serviceProvider.ServiceProvider.GetRequiredService<SharedPolicyFactory>();
            pipelineBuilder
                .AddPipeline(policyFactory.GetCircuitBreakerPolicy(
                    eventsAllowedBeforeBreaking: 2,
                    durationOfBreakInSeconds: 30)) // Circuit breaker
                .AddPipeline(policyFactory.GetRetryPolicy(retryCount: 3)); // Retry
        });
        // Register infrastructure services
        services.AddScoped<IRedisCacheService, RedisCacheService>();
        //  services.AddScoped<IAudioConversionService, AudioConversionService>();
        services.AddSingleton(typeof(ICache<,>), typeof(LimitedMemoryCache<,>));

        return services;
    }
} 