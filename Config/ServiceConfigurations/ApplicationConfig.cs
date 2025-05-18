using ElevenLabs;
using MyTts.Repositories;
using MyTts.Services;
using MyTts.Services.Interfaces;

namespace MyTts.Config.ServiceConfigurations;

public static class ApplicationConfig
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IMp3Repository, Mp3Repository>();
        
        // Register core services
        services.AddScoped<IMp3Service, Mp3Service>();
        services.AddScoped<ITtsManagerService, TtsManagerService>();
        services.AddScoped<IMp3StreamMerger, Mp3StreamMerger>();
        services.AddScoped<INewsFeedsService, NewsFeedsService>();
        services.AddScoped<ILocalStorageService, LocalStorageService>();

        // Register infrastructure services
        services.AddScoped<IRedisCacheService, RedisCacheService>();
        services.AddScoped<IFileStreamingService, FileStreamingService>();
        services.AddScoped<IAudioConversionService, AudioConversionService>();

        // Register external clients
        services.AddScoped<ElevenLabsClient>();

        return services;
    }
} 