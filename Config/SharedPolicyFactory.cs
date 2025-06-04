using System.Net.Mail;
using MyTts.Services.Interfaces;
using Polly;
using Polly.Retry;
using Polly.Extensions.Http;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace MyTts.Config.ServiceConfigurations
{
    public class SharedPolicyFactory
    {
        private readonly ILogger<SharedPolicyFactory> _logger;
        private readonly IServiceProvider _serviceProvider;
        private static readonly ResiliencePropertyKey<string> RecipientKey = new("recipient");

        public SharedPolicyFactory(ILogger<SharedPolicyFactory> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        private async Task TrySendNotificationAsync(string title, string message, NotificationType type)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
                await notificationService.SendNotificationAsync(title, message, type);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send notification: {Title}", title);
            }
        }

        private RetryStrategyOptions<T> CreateRetryStrategy<T>(
            Func<RetryPredicateArguments<T>, ValueTask<bool>> shouldHandle,
            int maxRetries,
            int baseDelaySeconds,
            bool useJitter = true,
            Func<OnRetryArguments<T>, Task>? onRetry = null)
        {
            return new RetryStrategyOptions<T>
            {
                ShouldHandle = shouldHandle,
                MaxRetryAttempts = maxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(baseDelaySeconds),
                UseJitter = useJitter,
                OnRetry = async args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "Retry {RetryCount} after {Delay}ms for {OperationKey}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Context?.OperationKey ?? "unknown");
                    
                    if (onRetry != null)
                    {
                        await onRetry(args);
                    }
                }
            };
        }

        private CircuitBreakerStrategyOptions<T> CreateCircuitBreakerStrategy<T>(
            Func<CircuitBreakerPredicateArguments<T>, ValueTask<bool>> shouldHandle,
            double failureThreshold,
            int minimumThroughput,
            int samplingDurationSeconds,
            int breakDurationSeconds,
            Func<OnCircuitOpenedArguments<T>, Task>? onOpened = null,
            Func<OnCircuitClosedArguments<T>, Task>? onClosed = null)
        {
            return new CircuitBreakerStrategyOptions<T>
            {
                ShouldHandle = shouldHandle,
                FailureRatio = failureThreshold,
                MinimumThroughput = minimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(samplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(breakDurationSeconds),
                OnOpened = async args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "Circuit breaker opened for {Duration}s",
                        breakDurationSeconds);
                    if (onOpened != null)
                    {
                        await onOpened(args);
                    }
                },
                OnClosed = async args =>
                {
                    _logger.LogInformation("Circuit breaker reset");
                    if (onClosed != null)
                    {
                        await onClosed(args);
                    }
                }
            };
        }

        #region HTTP Policies
        public ResiliencePipeline<HttpResponseMessage> GetRetryPolicy(
            int retryCount = 3,
            double baseDelaySeconds = 2)
        {
            return new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddRetry(CreateRetryStrategy<HttpResponseMessage>(
                    args => new ValueTask<bool>(
                        args.Outcome.Exception is HttpRequestException ||
                        args.Outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests),
                    retryCount,
                    (int)baseDelaySeconds))
                .Build();
        }

        public ResiliencePipeline<HttpResponseMessage> GetCircuitBreakerPolicy(
            int eventsAllowedBeforeBreaking = 5,
            int durationOfBreakInSeconds = 30)
        {
            return new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddCircuitBreaker(CreateCircuitBreakerStrategy<HttpResponseMessage>(
                    args => new ValueTask<bool>(args.Outcome.Exception is HttpRequestException),
                    0.5,
                    eventsAllowedBeforeBreaking,
                    30,
                    durationOfBreakInSeconds))
                .Build();
        }
        #endregion

        #region Email Policies
        public ResiliencePipeline<T> GetEmailPolicy<T>()
        {
            var retryPolicy = GetEmailRetryPolicy<T>();
            var circuitBreakerPolicy = GetEmailCircuitBreakerPolicy<T>();
            return new ResiliencePipelineBuilder<T>()
                .AddPipeline(retryPolicy)
                .AddPipeline(circuitBreakerPolicy)
                .Build();
        }

        public ResiliencePipeline<T> GetEmailRetryPolicy<T>(
            int maxRetries = 5,
            int retryDelaySeconds = 5)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddRetry(CreateRetryStrategy<T>(
                    args => new ValueTask<bool>(
                        args.Outcome.Exception is SmtpException smtpEx && 
                        (smtpEx.StatusCode == SmtpStatusCode.ServiceNotAvailable || 
                         smtpEx.StatusCode == SmtpStatusCode.ServiceClosingTransmissionChannel ||
                         smtpEx.StatusCode == SmtpStatusCode.ExceededStorageAllocation ||
                         smtpEx.StatusCode == SmtpStatusCode.MailboxBusy ||
                         smtpEx.StatusCode == SmtpStatusCode.MailboxUnavailable ||
                         smtpEx.StatusCode == SmtpStatusCode.TransactionFailed) ||
                        args.Outcome.Exception is InvalidOperationException),
                    maxRetries,
                    retryDelaySeconds,
                    onRetry: async args =>
                    {
                        var recipient = args.Context?.Properties.TryGetValue(
                            RecipientKey,
                            out var value) == true ? value : "unknown";
                        
                        var smtpEx = args.Outcome.Exception as SmtpException;
                        var statusCode = smtpEx?.StatusCode.ToString() ?? "Unknown";
                        
                        await TrySendNotificationAsync(
                            "Email Retry",
                            $"Retrying email to {recipient} after {args.RetryDelay.TotalMilliseconds}ms (Status: {statusCode})",
                            NotificationType.Warning);
                    }))
                .Build();
        }

        public ResiliencePipeline<T> GetEmailCircuitBreakerPolicy<T>(
            int exceptionsAllowedBeforeBreaking = 2,
            int durationOfBreakInMinutes = 5)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddCircuitBreaker(CreateCircuitBreakerStrategy<T>(
                    args => new ValueTask<bool>(
                        args.Outcome.Exception is SmtpException ||
                        args.Outcome.Exception is InvalidOperationException),
                    0.5,
                    exceptionsAllowedBeforeBreaking,
                    durationOfBreakInMinutes * 60,
                    durationOfBreakInMinutes * 60,
                    onOpened: async args => await TrySendNotificationAsync(
                        "Email Circuit Breaker Opened",
                        $"Service unavailable for {durationOfBreakInMinutes}m",
                        NotificationType.Warning),
                    onClosed: async args => await TrySendNotificationAsync(
                        "Email Circuit Breaker Reset",
                        "Service is healthy again",
                        NotificationType.Success)))
                .Build();
        }
        #endregion

        #region Storage Policies
        public ResiliencePipeline<T> GetStorageRetryPolicy<T>(
            int maxRetries = 3,
            int retryDelaySeconds = 5)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddRetry(CreateRetryStrategy<T>(
                    args => new ValueTask<bool>(
                        args.Outcome.Exception is IOException ||
                        args.Outcome.Exception is UnauthorizedAccessException),
                    maxRetries,
                    retryDelaySeconds,
                    onRetry: async args => await TrySendNotificationAsync(
                        "Storage Retry",
                        $"Retrying operation after {args.RetryDelay.TotalMilliseconds}ms (attempt {args.AttemptNumber})",
                        NotificationType.Warning)))
                .Build();
        }
        #endregion

        #region TTS Policies
        public ResiliencePipeline<T> CreateTtsPipeline<T>(int retryCount = 3, int baseDelaySeconds = 2)
        {
            var retryPolicy = GetTtsRetryPolicy<T>(retryCount, baseDelaySeconds);
            var circuitBreakerPolicy = GetTtsCircuitBreakerPolicy<T>();
            return new ResiliencePipelineBuilder<T>()
                .AddPipeline(retryPolicy)
                .AddPipeline(circuitBreakerPolicy)
                .Build();
        }

        public ResiliencePipeline<T> GetTtsRetryPolicy<T>(
            int retryCount = 3,
            int baseDelaySeconds = 2)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddRetry(CreateRetryStrategy<T>(
                    args => new ValueTask<bool>(
                        args.Outcome.Exception is HttpRequestException ||
                        args.Outcome.Exception is TimeoutException),
                    retryCount,
                    baseDelaySeconds,
                    onRetry: async args => await TrySendNotificationAsync(
                        "TTS Retry",
                        $"Retrying operation after {baseDelaySeconds * Math.Pow(2, args.AttemptNumber)}s (attempt {args.AttemptNumber})",
                        NotificationType.Warning)))
                .Build();
        }

        public ResiliencePipeline<T> GetTtsCircuitBreakerPolicy<T>(
            double failureThreshold = 0.5,
            int minimumThroughput = 10,
            int samplingDurationSeconds = 30,
            int breakDurationSeconds = 30)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddCircuitBreaker(CreateCircuitBreakerStrategy<T>(
                    args => new ValueTask<bool>(
                        args.Outcome.Exception is HttpRequestException ||
                        args.Outcome.Exception is TimeoutException),
                    failureThreshold,
                    minimumThroughput,
                    samplingDurationSeconds,
                    breakDurationSeconds,
                    onOpened: async args => await TrySendNotificationAsync(
                        "TTS Circuit Breaker Opened",
                        $"Service unavailable for {breakDurationSeconds}s",
                        NotificationType.Warning),
                    onClosed: async args => await TrySendNotificationAsync(
                        "TTS Circuit Breaker Reset",
                        "Service is healthy again",
                        NotificationType.Success)))
                .Build();
        }
        #endregion

        #region Redis Policies
        public ResiliencePipeline<RedisValue> GetRedisRetryPolicy(int maxRetries, int retryDelayMilliseconds)
        {
            return new ResiliencePipelineBuilder<RedisValue>()
                .AddRetry(CreateRetryStrategy<RedisValue>(
                    args => new ValueTask<bool>(
                        args.Outcome.Exception is RedisConnectionException ||
                        args.Outcome.Exception is TimeoutException),
                    maxRetries,
                    retryDelayMilliseconds / 1000,
                    onRetry: async args => await TrySendNotificationAsync(
                        "Redis Retry",
                        $"Retrying operation after {args.RetryDelay.TotalMilliseconds}ms (attempt {args.AttemptNumber})",
                        NotificationType.Warning)))
                .Build();
        }

        public ResiliencePipeline<T> GetRedisCircuitBreakerPolicy<T>(
            double failureThreshold = 0.5,
            int minimumThroughput = 5,
            int samplingDurationSeconds = 60,
            int breakDurationSeconds = 30)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddCircuitBreaker(CreateCircuitBreakerStrategy<T>(
                    args => new ValueTask<bool>(
                        args.Outcome.Exception is RedisConnectionException ||
                        args.Outcome.Exception is TimeoutException),
                    failureThreshold,
                    minimumThroughput,
                    samplingDurationSeconds,
                    breakDurationSeconds,
                    onOpened: async args => await TrySendNotificationAsync(
                        "Redis Circuit Breaker Opened",
                        $"Service unavailable for {breakDurationSeconds}s",
                        NotificationType.Warning),
                    onClosed: async args => await TrySendNotificationAsync(
                        "Redis Circuit Breaker Reset",
                        "Service is healthy again",
                        NotificationType.Success)))
                .Build();
        }
        #endregion
    }
}