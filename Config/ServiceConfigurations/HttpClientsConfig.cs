using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using System;
using System.Net.Http;

namespace MyTts.Config.ServiceConfigurations
{
    public static class HttpClientsConfig
    {
        public static IServiceCollection AddHttpClients(this IServiceCollection services)
        {
            // Example for ElevenLabs (if you have one)
            // services.AddHttpClient("ElevenLabsClient")
            //    .SetHandlerLifetime(TimeSpan.FromMinutes(5)) // Set lifetime of HttpMessageHandler
            //    .AddPolicyHandler(GetRetryPolicy())
            //    .AddPolicyHandler(GetCircuitBreakerPolicy());

            services.AddHttpClient("GeminiTtsClient")
               .SetHandlerLifetime(TimeSpan.FromMinutes(5))
               .AddPolicyHandler(GetRetryPolicy()) // Standard retry policy
               .AddPolicyHandler(GetCircuitBreakerPolicy()); // Standard circuit breaker

            return services;
        }

        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError() // Handles HttpRequestException, 5xx, and 408
                .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // Handle 429
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        // Log retry attempts if needed
                        // var logger = context.GetLogger(); 
                        // logger?.LogWarning($"Delaying for {timespan.TotalMilliseconds}ms, then making retry {retryAttempt}.");
                    });
        }

        private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5, // Number of consecutive failed attempts
                    durationOfBreak: TimeSpan.FromSeconds(30) // How long the circuit stays open
                );
        }
    }
}