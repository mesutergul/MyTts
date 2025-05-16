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

        public RedisCacheService(
            IConnectionMultiplexer redis,
            IOptions<RedisConfig> config,
            ILogger<RedisCacheService> logger)
        {

            _redis = redis;
            _config = config.Value;
            _logger = logger;
            if (redis == null || !redis.IsConnected)
            {
                _logger.LogError("Redis ConnectionMultiplexer is not connected. RedisCacheService cannot operate.");
                // Throwing here is okay, as this service should only be instantiated
                // when the ConnectionMultiplexer is ready. The DI container will then
                // use the fallback.
                throw new InvalidOperationException("Redis ConnectionMultiplexer is not connected.");
            }
            _db = redis.GetDatabase(_config.DatabaseId);
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected) // Defensive check, though DI should prevent this
            {
                _logger.LogWarning("Redis connection not available for GET operation. Key: {Key}", key);
                return default;
            }
            // Get the value from Redis using the formatted key
            var value = await _db.StringGetAsync(GetKey(key));

            // Return default(T) if the value doesn't exist
            if (value.IsNull)
                return default;

            // Directly deserialize the string value to type T
            try
            {
                string jsonString = value.ToString();
                return JsonSerializer.Deserialize<T>(jsonString);
            }
            catch (JsonException)
            {
                // Log the error if needed
                _logger.LogError($"Failed to deserialize value for key {key}");
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected) // Defensive check, though DI should prevent this
            {
                _logger.LogWarning("Redis connection not available for SET operation. Key: {Key}", key);
                return;
            }
            try
            {
                var serializedValue = JsonSerializer.Serialize(value);
                await _db.StringSetAsync(
                    GetKey(key),
                    serializedValue,
                    expiry ?? TimeSpan.FromMinutes(_config.DefaultExpirationMinutes)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis SET failed for key: {Key}", key);
                //throw;
            }
        }

        public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for REMOVE operation. Key: {Key}", key);
                return false;
            }
            try
            {
                return await _db.KeyDeleteAsync(GetKey(key));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis REMOVE failed for key: {Key}", key);
                return false;
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            if (_db == null || !_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection not available for EXISTS operation. Key: {Key}", key);
                return false;
            }
            try
            {
                return await _db.KeyExistsAsync(GetKey(key));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis EXISTS check failed for key: {Key}", key);
                return false;
            }
        }
        public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_redis.IsConnected);
        }
        private string GetKey(string key) => $"{_config.InstanceName}{key}";
    }
}