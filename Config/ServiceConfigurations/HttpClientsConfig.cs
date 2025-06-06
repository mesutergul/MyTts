using Polly;

namespace MyTts.Config.ServiceConfigurations;

public static class HttpClientsConfig
{
    public static IServiceCollection AddHttpClientsServices(this IServiceCollection services)
    {
        // services.AddHttpClient("CloudTtsClient")
        //            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
        //            .AddPolicyHandler(GetRetryPolicy()) // Standard retry policy
        //            .AddPolicyHandler(GetCircuitBreakerPolicy()); // Standard circuit breaker

        // Configure Firebase storage client with resilience
        services.AddHttpClient("FirebaseStorage")
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .AddResilienceHandler("FirebaseStorageCombinedPolicy", (builder, provider) =>
            {
                var policyFactory = provider.ServiceProvider.GetRequiredService<SharedPolicyFactory>();
                builder
                .AddPipeline(policyFactory.GetCircuitBreakerPolicy(
                    eventsAllowedBeforeBreaking: 2,
                    durationOfBreakInSeconds: 30)) // Circuit breaker
                .AddPipeline(policyFactory.GetRetryPolicy(retryCount: 3)); // Retry
            });
        return services;
    }
    // private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    //     {
    //         return HttpPolicyExtensions
    //             .HandleTransientHttpError() // Handles HttpRequestException, 5xx, and 408
    //             .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // Handle 429
    //             .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
    //                 onRetry: (outcome, timespan, retryAttempt, context) =>
    //                 {
    //                     // Log retry attempts if needed
    //                     // var logger = context.GetLogger(); 
    //                     // logger?.LogWarning($"Delaying for {timespan.TotalMilliseconds}ms, then making retry {retryAttempt}.");
    //                 });
    //     }

    //     private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    //     {
    //         return HttpPolicyExtensions
    //             .HandleTransientHttpError()
    //             .CircuitBreakerAsync(
    //                 handledEventsAllowedBeforeBreaking: 5, // Number of consecutive failed attempts
    //                 durationOfBreak: TimeSpan.FromSeconds(30) // How long the circuit stays open
    //             );
    //     }
}