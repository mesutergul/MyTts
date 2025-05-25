using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyTts.Config;
using MyTts.Services.Interfaces; // Add this
using MyTts.Services.Clients;   // Add this

namespace MyTts.Config.ServiceConfigurations
{
    public static class GeminiAiServiceConfig
    {
        public static IServiceCollection AddGeminiAiConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            // Bind GeminiAiConfig from appsettings.json
            services.Configure<GeminiAiConfig>(configuration.GetSection("GeminiAi"));
            
            // Register the Gemini TTS Client
            // It depends on IOptions<GeminiAiConfig>, ILogger<GeminiTtsClient>, and IHttpClientFactory
            // IHttpClientFactory is typically registered via services.AddHttpClients() in HttpClientsConfig.cs
            services.AddScoped<IGeminiTtsClient, GeminiTtsClient>();
            
            return services;
        }
    }
}
