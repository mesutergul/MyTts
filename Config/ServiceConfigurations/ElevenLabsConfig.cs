using Microsoft.Extensions.Options;
using MyTts.Config;

namespace MyTts.Config.ServiceConfigurations;

public static class ElevenLabsServiceConfig
{
    public static IServiceCollection AddElevenLabsServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register ElevenLabs configuration
        services.AddOptions<ElevenLabsConfig>()
            .Bind(configuration.GetSection("ElevenLabs"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register ElevenLabsClient with better error handling
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ElevenLabsConfig>>().Value;

            // First try configuration, then environment variable
            var apiKey = config.ApiKey ??
                        Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY") ??
                        throw new InvalidOperationException("ElevenLabs API key not found");

            return new ElevenLabs.ElevenLabsClient(
                new ElevenLabs.ElevenLabsAuthentication(apiKey),
                new ElevenLabs.ElevenLabsClientSettings("api.elevenlabs.io", "v1"),
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ElevenLabsClient")
            );
        });

        return services;
    }
} 