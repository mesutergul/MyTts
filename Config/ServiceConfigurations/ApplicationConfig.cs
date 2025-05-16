using MyTts.Services;
using MyTts.Repositories;
using ElevenLabs;

namespace MyTts.Config.ServiceConfigurations;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IMp3Repository, Mp3Repository>();
        
        // Register services
        services.AddScoped<IMp3Service, Mp3Service>();
        services.AddScoped<IFileStreamingService, FileStreamingService>();
        services.AddScoped<ITtsManagerService, TtsManagerService>();
        services.AddScoped<IMp3StreamMerger, Mp3StreamMerger>();
        services.AddScoped<NewsFeedsService>();
        services.AddScoped<IRedisCacheService, RedisCacheService>();

        // Register ElevenLabs client
        services.AddScoped<ElevenLabsClient>();

        return services;
    }
} 