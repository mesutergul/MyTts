using Microsoft.Extensions.Logging;
using MyTts.Config.ServiceConfigurations;
using Polly;
using Polly.CircuitBreaker; // Required for BrokenCircuitException
using StackExchange.Redis; // Required for RedisConnectionException
using System; // Required for TimeoutException
using System.Threading.Tasks; // Required for Task
using System.Threading; // Required for CancellationToken

namespace MyTts.Services
{
    public class RedisCircuitBreaker
    {
        private readonly ILogger<RedisCircuitBreaker> _logger;
        private readonly ResiliencePipeline<object> _circuitBreakerPolicy;

        public RedisCircuitBreaker(
            ILogger<RedisCircuitBreaker> logger,
            SharedPolicyFactory policyFactory)
        {
            _logger = logger;
            // Initialize the circuit breaker policy using the factory
            // Pass default values or specific values if needed by GetRedisCircuitBreakerPolicy
            _circuitBreakerPolicy = policyFactory.GetRedisCircuitBreakerPolicy<object>();
        }

        public async Task<T?> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string operationName, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _circuitBreakerPolicy.ExecuteAsync<T>(async (token) =>
                {
                    try
                    {
                        return await action(token);
                    }
                    catch (RedisConnectionException ex) // More specific exception handling
                    {
                        _logger.LogError(ex, "Redis connection failed during operation {OperationName}", operationName);
                        throw; // Re-throw to be caught by Polly
                    }
                    catch (TimeoutException ex) // More specific exception handling
                    {
                        _logger.LogError(ex, "Redis operation {OperationName} timed out", operationName);
                        throw; // Re-throw to be caught by Polly
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Redis operation {OperationName} failed with an unexpected exception", operationName);
                        throw; // Re-throw to be caught by Polly or the outer handler
                    }
                }, cancellationToken);
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Circuit breaker is open - Redis operation {OperationName} was not attempted", operationName);
                return default;
            }
            // It's generally better to let Polly handle exceptions that it's configured for.
            // If an exception makes it past Polly, it means it wasn't one of the handled exceptions (RedisConnectionException, TimeoutException)
            // or it's a non-transient error that shouldn't be retried by this mechanism.
            // The logging for these specific exceptions is now inside the lambda.
            // The `OnOpened` event in the policy will log when the circuit breaks.
        }

        public async Task ExecuteAsync(Func<CancellationToken, Task> action, string operationName, CancellationToken cancellationToken = default)
        {
            try
            {
                await _circuitBreakerPolicy.ExecuteAsync(async (token) =>
                {
                    try
                    {
                        await action(token);
                    }
                    catch (RedisConnectionException ex) // More specific exception handling
                    {
                        _logger.LogError(ex, "Redis connection failed during operation {OperationName}", operationName);
                        throw; // Re-throw to be caught by Polly
                    }
                    catch (TimeoutException ex) // More specific exception handling
                    {
                        _logger.LogError(ex, "Redis operation {OperationName} timed out", operationName);
                        throw; // Re-throw to be caught by Polly
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Redis operation {OperationName} failed with an unexpected exception", operationName);
                        throw; // Re-throw to be caught by Polly or the outer handler
                    }
                }, cancellationToken);
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Circuit breaker is open - Redis operation {OperationName} was not attempted", operationName);
            }
            // Similar to the generic version, specific exception logging is now within the lambda.
            // The `OnOpened` event in the policy will log when the circuit breaks.
        }
    }
}