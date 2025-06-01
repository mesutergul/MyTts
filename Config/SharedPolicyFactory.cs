using System.Net.Mail;
using MyTts.Services.Interfaces;
using Polly;
using Polly.Retry;
using Polly.Extensions.Http;
using Polly.CircuitBreaker;

namespace MyTts.Config.ServiceConfigurations
{

    public class SharedPolicyFactory
    {
        private readonly ILogger<SharedPolicyFactory> _logger;
        private readonly INotificationService _notificationService;

        public SharedPolicyFactory(ILogger<SharedPolicyFactory> logger, INotificationService notificationService)
        {
            _logger = logger;
            _notificationService = notificationService;
        }
       

        #region HTTP Policies
        public ResiliencePipeline<HttpResponseMessage> GetRetryPolicy(
            int retryCount = 3,
            double baseDelaySeconds = 2)
        {
            return new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    ShouldHandle = args => new ValueTask<bool>(
                        args.Outcome.Exception is HttpRequestException ||
                        args.Outcome.Result?.StatusCode == System.Net.HttpStatusCode.TooManyRequests),
                    MaxRetryAttempts = retryCount,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(baseDelaySeconds),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        _logger.LogWarning(
                            "HTTP retry {RetryAttempt} after {Delay}ms for {OperationKey}",
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds,
                            args.Context?.OperationKey ?? "unknown");
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }

        public ResiliencePipeline<HttpResponseMessage> GetCircuitBreakerPolicy(
            int eventsAllowedBeforeBreaking = 5,
            int durationOfBreakInSeconds = 30)
        {
            return new ResiliencePipelineBuilder<HttpResponseMessage>()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    ShouldHandle = args => new ValueTask<bool>(
                        args.Outcome.Exception is HttpRequestException),
                    FailureRatio = 0.5,
                    MinimumThroughput = eventsAllowedBeforeBreaking,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(durationOfBreakInSeconds),
                    OnOpened = args =>
                    {
                        _logger.LogWarning(
                            args.Outcome.Exception,
                            "Circuit breaker opened. Blocking requests for {Duration}s.",
                            durationOfBreakInSeconds);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        _logger.LogInformation("Circuit breaker reset - service is healthy");
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }
        #endregion

        #region Email Policies
        public  ResiliencePipeline<T> GetEmailRetryPolicy<T>(
            int maxRetries = 3,
            int retryDelaySeconds = 2)
        {
            ResiliencePropertyKey<string> RecipientKey = new("recipient");

            return new ResiliencePipelineBuilder<T>()
                .AddRetry(new RetryStrategyOptions<T>
                {
                    ShouldHandle = args => new ValueTask<bool>(
                        args.Outcome.Exception is SmtpException ||
                        args.Outcome.Exception is InvalidOperationException),
                    MaxRetryAttempts = maxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(retryDelaySeconds),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        var recipient = args.Context?.Properties.TryGetValue(
                            RecipientKey,
                            out var value) == true ? value : "unknown";
                        _logger.LogWarning(args.Outcome.Exception,
                            "Email retry {RetryCount} after {Delay}ms for recipient {Recipient}",
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds,
                            recipient);
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }

        public  ResiliencePipeline<T> GetEmailCircuitBreakerPolicy<T>(
            int exceptionsAllowedBeforeBreaking = 2,
            int durationOfBreakInMinutes = 5)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
                {
                    ShouldHandle = args => new ValueTask<bool>(
                        args.Outcome.Exception is SmtpException ||
                        args.Outcome.Exception is InvalidOperationException),
                    FailureRatio = 0.5,
                    MinimumThroughput = exceptionsAllowedBeforeBreaking,
                    SamplingDuration = TimeSpan.FromMinutes(durationOfBreakInMinutes),
                    BreakDuration = TimeSpan.FromMinutes(durationOfBreakInMinutes),
                    OnOpened = args =>
                    {
                        _logger.LogWarning(args.Outcome.Exception,
                            "Email circuit breaker opened for {Duration}m",
                            durationOfBreakInMinutes);
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        _logger.LogInformation("Email circuit breaker reset");
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = _ =>
                    {
                        _logger.LogInformation("Email circuit breaker half-open - testing service health");
                        return ValueTask.CompletedTask;
                    }
                })
                .Build();
        }
        #endregion
        #region Storage Policies
        public  ResiliencePipeline<T> GetStorageRetryPolicy<T>(
            int maxRetries = 3,
            int retryDelaySeconds = 5)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddRetry(new RetryStrategyOptions<T>
                {
                    ShouldHandle = args => new ValueTask<bool>(
                        args.Outcome.Exception is IOException ||
                        args.Outcome.Exception is UnauthorizedAccessException),
                    MaxRetryAttempts = maxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(retryDelaySeconds),
                    OnRetry = async args =>
                    {
                        _logger.LogWarning(args.Outcome.Exception,
                            "Storage retry {RetryCount} after {Delay}ms for {OperationKey}",
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds,
                            args.Context?.OperationKey ?? "unknown");

                        await _notificationService.SendNotificationAsync(
                            "Storage Retry",
                            $"Retrying operation after {args.RetryDelay.TotalMilliseconds}ms (attempt {args.AttemptNumber})",
                            NotificationType.Warning);
                    }
                })
                .Build();
        }
        #endregion

        #region TTS Policies
        public  ResiliencePipeline<T> GetTtsRetryPolicy<T>(
            int retryCount = 3,
            int baseDelaySeconds = 2)
        {

            return new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                ShouldHandle = args => new ValueTask<bool>(
                args.Outcome.Exception is HttpRequestException ||
                args.Outcome.Exception is TimeoutException
                ),
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = retryCount,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(baseDelaySeconds),
                OnRetry = async args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "TTS retry {RetryCount} after {Delay}ms for {OperationKey}",
                        args.AttemptNumber, baseDelaySeconds * Math.Pow(2, args.AttemptNumber), args.Context?.OperationKey ?? "unknown");

                    await _notificationService.SendNotificationAsync(
                        "TTS Retry",
                        $"Retrying operation after {baseDelaySeconds * Math.Pow(2, args.AttemptNumber)}s (attempt {args.AttemptNumber})",
                        NotificationType.Warning);
                }
            })
            .Build();
        }

        public  ResiliencePipeline<T> GetTtsCircuitBreakerPolicy<T>(
            double failureThreshold = 0.5,
            int minimumThroughput = 10,
            int samplingDurationSeconds = 30,
            int breakDurationSeconds = 30)
        {
            return new ResiliencePipelineBuilder<T>()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
                {
                    ShouldHandle = args => new ValueTask<bool>(
                        args.Outcome.Exception is HttpRequestException ||
                        args.Outcome.Exception is TimeoutException
                    ),
                    FailureRatio = failureThreshold,
                    MinimumThroughput = minimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(samplingDurationSeconds),
                    BreakDuration = TimeSpan.FromSeconds(breakDurationSeconds),
                    OnOpened = async args =>
                    {
                        _logger.LogError(args.Outcome.Exception,
                            "TTS circuit breaker opened for {Duration}s",
                            breakDurationSeconds);

                        await _notificationService.SendNotificationAsync(
                            "TTS Circuit Breaker Opened",
                            $"Service unavailable for {breakDurationSeconds}s",
                            NotificationType.Warning);
                    },
                    OnClosed = async args =>
                    {
                        _logger.LogInformation("TTS circuit breaker reset");
                        await _notificationService.SendNotificationAsync(
                            "TTS Circuit Breaker Reset",
                            "Service is healthy again",
                            NotificationType.Success);
                    }
                })
            .Build();
        }
        #endregion
    }

}