using StackExchange.Redis;
using System.Text.Json;
using MyTts.Config;
using Microsoft.Extensions.Options;

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
            _db = redis.GetDatabase(_config.DatabaseId);
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            try
            {
                var value = await _db.StringGetAsync(GetKey(key));
                return value.HasValue 
                    ? JsonSerializer.Deserialize<T>(value) 
                    : default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting value for key: {Key}", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
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

        public async Task<bool> RemoveAsync(string key)
        {
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

        public async Task<bool> ExistsAsync(string key)
        {
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
        public Task<bool> IsConnectedAsync()
        {
            return Task.FromResult(_redis.IsConnected);
        }
        private string GetKey(string key) => $"{_config.InstanceName}{key}";
    }
}