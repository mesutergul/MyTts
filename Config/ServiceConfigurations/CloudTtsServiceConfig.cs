using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyTts.Config;
using MyTts.Services.Interfaces; // Add this
using MyTts.Services.Clients;   // Add this

namespace MyTts.Config.ServiceConfigurations
{
    public static class CloudTtsServiceConfig
    {
        public static IServiceCollection AddCloudTtsConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            // Bind CloudTtsConfig from appsettings.json
            services.Configure<CloudTtsConfig>(configuration.GetSection("CloudTtsConfig"));

            // Register the Cloud TTS Client
            // It depends on IOptions<CloudTtsConfig>, ILogger<CloudTtsClient>, and IHttpClientFactory
            // IHttpClientFactory is typically registered via services.AddHttpClients() in HttpClientsConfig.cs
            services.AddScoped<ICloudTtsClient, CloudTtsClient>();
            
            return services;
        }
    }
}