using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services.Interfaces;
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

        public RedisCacheService(
            IConnectionMultiplexer redis,
            IOptions<RedisConfig> config,
            ILogger<RedisCacheService> logger,
            ILogger<RedisCircuitBreaker> circuitBreakerLogger)
        {
            _redis = redis;
            _config = config.Value;
            _logger = logger;
            _circuitBreaker = new RedisCircuitBreaker(circuitBreakerLogger);

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

            return await _circuitBreaker.ExecuteAsync(async () =>
            {
                var value = await _db.StringGetAsync(GetKey(key));
                if (value.IsNull)
                    return default;

                try
                {
                    string jsonString = value.ToString();
                    return JsonSerializer.Deserialize<T>(jsonString);
                }
                catch (JsonException)
                {
                    _logger.LogError("Failed to deserialize value for key {Key}", key);
                    return default;
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

            await _circuitBreaker.ExecuteAsync(async () =>
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

            return await _circuitBreaker.ExecuteAsync(async () =>
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

            return await _circuitBreaker.ExecuteAsync(async () =>
            {
                return await _db.KeyExistsAsync(GetKey(key));
            }, $"EXISTS_{key}");
        }

        public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_redis.IsConnected);
        }

        private string GetKey(string key) => $"{_config.InstanceName}{key}";
    }
}