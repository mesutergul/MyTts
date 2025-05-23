using MyTts.Repositories;
using MyTts.Services;
using MyTts.Services.Interfaces;
using MyTts.Services.Clients;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;

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
        
        // Configure and register notification service with resilience policies
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.AddHttpClient<INotificationService, NotificationService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "MyTts-NotificationService");
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddTransientHttpErrorPolicy(policy => policy
            .WaitAndRetryAsync(3, retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))
        .AddTransientHttpErrorPolicy(policy => policy
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 2,
                durationOfBreak: TimeSpan.FromSeconds(30)));

        // Register infrastructure services
        services.AddScoped<IRedisCacheService, RedisCacheService>();
        services.AddScoped<IFileStreamingService, FileStreamingService>();
        //  services.AddScoped<IAudioConversionService, AudioConversionService>();
        services.AddSingleton(typeof(ICache<,>), typeof(LimitedMemoryCache<,>));

        return services;
    }
} 