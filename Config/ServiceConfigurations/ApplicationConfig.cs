using MyTts.Repositories;
using MyTts.Services;
using MyTts.Services.Interfaces;

namespace MyTts.Config.ServiceConfigurations;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register repositories
        services.AddScoped<IMp3Repository, Mp3Repository>();
        
        // Register application services
        services.AddScoped<IMp3Service, Mp3Service>();
        services.AddScoped<INotificationService, NotificationService>();
        
        // Configure notification options
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));

        // Register HttpClient for Slack notifications
        services.AddHttpClient<INotificationService, NotificationService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "MyTts-NotificationService");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
       
        // Register infrastructure services
        services.AddScoped<IRedisCacheService, RedisCacheService>();
        services.AddScoped<IFileStreamingService, FileStreamingService>();
      //  services.AddScoped<IAudioConversionService, AudioConversionService>();

        return services;
    }
} 