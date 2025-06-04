using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Config.ServiceConfigurations;
using MyTts.Services.Interfaces;
using Polly;
using StackExchange.Redis;
using System.Text.Json;

namespace MyTts.Services
{
    public class RedisCacheService : IRedisCacheService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly RedisConfig _config;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly RedisCircuitBreaker _circuitBreaker;
        private readonly ResiliencePipeline<RedisValue> _retryPolicy;

        public RedisCacheService(
            IConnectionMultiplexer redis,
            IOptions<RedisConfig> config,
            ILogger<RedisCacheService> logger,
            ILogger<RedisCircuitBreaker> loggerCircuit, // Added
            SharedPolicyFactory policyFactory, // Added
            ResiliencePipeline<RedisValue> retryPolicy)
        {
            _redis = redis;
            _config = config.Value;
            _logger = logger;
            _retryPolicy = retryPolicy;
            // Updated RedisCircuitBreaker instantiation
            _circuitBreaker = new RedisCircuitBreaker(loggerCircuit, policyFactory);

            if (redis == null || !redis.IsConnected)
            {
                _logger.LogError("Redis ConnectionMultiplexer is not connected. RedisCacheService cannot operate.");
                throw new InvalidOperationException("Redis ConnectionMultiplexer is not connected.");
            }
            _db = redis.GetDatabase(_config.DatabaseId);
        }

public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for GET operation. Key: {Key}", key);
                return default;
            }

           return await _circuitBreaker.ExecuteAsync(async (cancellationToken) =>
            {
                ResiliencePropertyKey<string> OperationKey = new("OperationKey");
                var context = ResilienceContextPool.Shared.Get(cancellationToken);
                context.Properties.Set(OperationKey, $"TTS_{key}");
                
                try
                {
                    var result = await _retryPolicy.ExecuteAsync(async (ctx) =>
                    {
                        return await _db.StringGetAsync(GetKey(key));
                    }, context);

                    if (result.IsNull)
                        return default;

                    try
                    {
                        string jsonString = result.ToString();
                        return JsonSerializer.Deserialize<T>(jsonString);
                    }
                    catch (JsonException)
                    {
                        _logger.LogError("Failed to deserialize value for key {Key}", key);
                        return default;
                    }
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(context);
                }
            }, $"GET_{key}");
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for SET operation. Key: {Key}", key);
                return;
            }

            await _circuitBreaker.ExecuteAsync(async (CancellationToken) =>
            {
                var serializedValue = JsonSerializer.Serialize(value);
                await _db.StringSetAsync(
                    GetKey(key),
                    serializedValue,
                    expiry ?? TimeSpan.FromMinutes(_config.DefaultExpirationMinutes)
                );
            }, $"SET_{key}");
        }

        public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for REMOVE operation. Key: {Key}", key);
                return false;
            }

            return await _circuitBreaker.ExecuteAsync(async (cancellationToken) =>
            {
                return await _db.KeyDeleteAsync(GetKey(key));
            }, $"REMOVE_{key}");
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for EXISTS operation. Key: {Key}", key);
                return false;
            }

            return await _circuitBreaker.ExecuteAsync(async (cancellationToken) =>
            {
                return await _db.KeyExistsAsync(GetKey(key));
            }, $"EXISTS_{key}");
        }

        public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_redis.IsConnected);
        }

        private string GetKey(string key) => $"{_config.InstanceName}{key}";
        public async Task SetBytesAsync(string key, byte[] value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for SET_BYTES operation. Key: {Key}", key);
                return;
            }

            await _circuitBreaker.ExecuteAsync(async (ct) =>
            {
                await _db.StringSetAsync(
                    GetKey(key),
                    value,
                    expiry ?? TimeSpan.FromMinutes(_config.DefaultExpirationMinutes)
                );
            }, $"SET_BYTES_{key}");
        }

        public async Task<byte[]?> GetBytesAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for GET_BYTES operation. Key: {Key}", key);
                return default;
            }

            return await _circuitBreaker.ExecuteAsync(async (ct) =>
            {
                ResiliencePropertyKey<string> OperationKey = new("OperationKey");
                var context = ResilienceContextPool.Shared.Get(cancellationToken);
                context.Properties.Set(OperationKey, $"TTS_BYTES_{key}");

                try
                {
                    var result = await _retryPolicy.ExecuteAsync(async (ctx) =>
                    {
                        return await _db.StringGetAsync(GetKey(key));
                    }, context);

                    if (result.IsNull)
                        return default;

                    return (byte[]?)result; // Cast RedisValue to byte[]
                }
                finally
                {
                    ResilienceContextPool.Shared.Return(context);
                }
            }, $"GET_BYTES_{key}");
        }
    }
}