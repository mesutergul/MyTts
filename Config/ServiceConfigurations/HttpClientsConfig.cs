using ElevenLabs;
using Microsoft.Extensions.Options;
using Polly;
using System.Net.Http.Headers;

namespace MyTts.Config.ServiceConfigurations;

public static class HttpClientsConfig
{
    public static IServiceCollection AddHttpClientsServices(this IServiceCollection services)
    {
        // Configure Firebase storage client with resilience
        services.AddHttpClient("FirebaseStorage", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("User-Agent", "MyTts/1.0");
        })
        .AddTransientHttpErrorPolicy(builder =>
            builder.WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))
        .AddTransientHttpErrorPolicy(builder =>
            builder.CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 2,
                durationOfBreak: TimeSpan.FromSeconds(30)));

        // Configure ElevenLabs client with proper settings
        services.AddHttpClient("ElevenLabsClient", (sp, client) =>
        {
            var settings = new ElevenLabsClientSettings("api.elevenlabs.io", "v1");
            var config = sp.GetRequiredService<IOptions<ElevenLabsConfig>>().Value;
            var logger = sp.GetRequiredService<ILogger<ElevenLabsClient>>();

            var apiKey = config.ApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogError("ElevenLabs API key not found in configuration or environment variables");
                throw new InvalidOperationException("ElevenLabs API key not found in configuration or environment variables");
            }

            client.BaseAddress = new Uri(settings.BaseRequestUrlFormat.Replace("{0}", ""));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("User-Agent", "MyTts");
            client.Timeout = TimeSpan.FromSeconds(60);
        })
        .AddTransientHttpErrorPolicy(builder =>
            builder.WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))
        .AddTransientHttpErrorPolicy(builder =>
            builder.CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 2,
                durationOfBreak: TimeSpan.FromSeconds(30)));

        return services;
    }
} 