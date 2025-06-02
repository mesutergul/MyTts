using Polly;
using Polly.CircuitBreaker;
using Microsoft.Extensions.Logging;
using MyTts.Config.ServiceConfigurations;
using StackExchange.Redis;

namespace MyTts.Services
{
    public class RedisCircuitBreaker
    {
        private readonly ILogger<RedisCircuitBreaker> _logger;
        private readonly SharedPolicyFactory _policyFactory;

        public RedisCircuitBreaker(
            ILogger<RedisCircuitBreaker> logger,
            SharedPolicyFactory policyFactory)
        {
            _logger = logger;
            _policyFactory = policyFactory;
        }

        public async Task<T?> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, string operationName, CancellationToken cancellationToken = default)
        {
            try
            {
                var policy = _policyFactory.GetRedisCircuitBreakerPolicy<T>();
                var context = ResilienceContextPool.Shared.Get(cancellationToken);
                try
                {
                    return await policy.ExecuteAsync(async (ctx) =>
                    {
                        try
                        {
                            return await action(ctx.CancellationToken);
                        }
                        catch (RedisConnectionException ex)
                        {
                            _logger.LogError(ex, "Redis connection failed during operation {OperationName}", operationName);
                            throw;
                        }
                        catch (TimeoutException ex)
                        {
                            _logger.LogError(ex, "Redis operation {OperationName} timed out", operationName);
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Redis operation {OperationName} failed with an unexpected exception", operationName);
                            throw;
                        }
                    }, context);
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(context);
                }
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Circuit breaker is open - Redis operation {OperationName} was not attempted", operationName);
                return default;
            }
        }

        public async Task ExecuteAsync(Func<CancellationToken, Task> action, string operationName, CancellationToken cancellationToken = default)
        {
            try
            {
                var policy = _policyFactory.GetRedisCircuitBreakerPolicy<object>();
                var context = ResilienceContextPool.Shared.Get(cancellationToken);
                try
                {
                    await policy.ExecuteAsync<object>(async (ctx) =>
                    {
                        try
                        {
                            await action(cancellationToken);
                            return null;
                        }
                        catch (RedisConnectionException ex)
                        {
                            _logger.LogError(ex, "Redis connection failed during operation {OperationName}", operationName);
                            throw;
                        }
                        catch (TimeoutException ex)
                        {
                            _logger.LogError(ex, "Redis operation {OperationName} timed out", operationName);
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Redis operation {OperationName} failed with an unexpected exception", operationName);
                            throw;
                        }
                    }, context);
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(context);
                }
            }
            catch (BrokenCircuitException)
            {
                _logger.LogWarning("Circuit breaker is open - Redis operation {OperationName} was not attempted", operationName);
            }
        }
    }
} 