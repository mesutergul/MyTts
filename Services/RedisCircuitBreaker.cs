using Polly;
using Polly.CircuitBreaker;
using Microsoft.Extensions.Logging;

namespace MyTts.Services
{
    public class RedisCircuitBreaker
    {
        private readonly ILogger<RedisCircuitBreaker> _logger;
        private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
        private readonly int _failureThreshold;
        private readonly TimeSpan _durationOfBreak;
        private readonly int _samplingDuration;

        public RedisCircuitBreaker(
            ILogger<RedisCircuitBreaker> logger,
            int failureThreshold = 5,
            int durationOfBreakSeconds = 30,
            int samplingDurationSeconds = 60)
        {
            _logger = logger;
            _failureThreshold = failureThreshold;
            _durationOfBreak = TimeSpan.FromSeconds(durationOfBreakSeconds);
            _samplingDuration = samplingDurationSeconds;

            _circuitBreakerPolicy = Policy
                .Handle<Exception>()
                .AdvancedCircuitBreakerAsync(
                    failureThreshold: _failureThreshold / 100.0, // Convert to percentage
                    samplingDuration: TimeSpan.FromSeconds(_samplingDuration),
                    minimumThroughput: _failureThreshold,
                    durationOfBreak: _durationOfBreak,
                    onBreak: (ex, duration) =>
                    {
                        _logger.LogWarning(ex, "Circuit breaker opened for {Duration} seconds after {FailureThreshold} failures in {SamplingDuration} seconds",
                            duration.TotalSeconds, _failureThreshold, _samplingDuration);
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Circuit breaker reset - Redis operations will be attempted again");
                    },
                    onHalfOpen: () =>
                    {
                        _logger.LogInformation("Circuit breaker is half-open - Testing Redis connection");
                    }
                );
        }

        public async Task<T?> ExecuteAsync<T>(Func<Task<T>> action, string operationName)
        {
            try
            {
                return await _circuitBreakerPolicy.ExecuteAsync(async () =>
                {
                    try
                    {
                        return await action();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Redis operation {OperationName} failed", operationName);
                        throw;
                    }
                });
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Circuit breaker is open - Redis operation {OperationName} was not attempted", operationName);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Redis operation {OperationName}", operationName);
                return default;
            }
        }

        public async Task ExecuteAsync(Func<Task> action, string operationName)
        {
            try
            {
                await _circuitBreakerPolicy.ExecuteAsync(async () =>
                {
                    try
                    {
                        await action();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Redis operation {OperationName} failed", operationName);
                        throw;
                    }
                });
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Circuit breaker is open - Redis operation {OperationName} was not attempted", operationName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Redis operation {OperationName}", operationName);
            }
        }
    }
} 