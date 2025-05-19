using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyTts.Services.Clients;
using MyTts.Services.Interfaces;
using MyTts.Config;
using MyTts.Services;
using ElevenLabs;

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