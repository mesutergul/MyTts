using MyTts.Services;

namespace MyTts.Services
{
    public class NullRedisCacheService : IRedisCacheService
    {
        private readonly ILogger<NullRedisCacheService> _logger;

        public NullRedisCacheService(ILogger<NullRedisCacheService> logger)
        {
            _logger = logger;
        }

        public Task<T?> GetAsync<T>(string key)
        {
            _logger.LogWarning("Redis is not available. Returning default value for key: {Key}", key);
            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            _logger.LogWarning("Redis is not available. Skipping cache set for key: {Key}", key);
            return Task.CompletedTask;
        }

        Task<bool> IRedisCacheService.RemoveAsync(string key)
        {
            _logger.LogWarning("Redis is not available. Skipping cache remove for key: {Key}", key);
            return Task.FromResult(false);
        }

        Task<bool> IRedisCacheService.ExistsAsync(string key)
        {
            _logger.LogWarning("Redis is not available. Skipping exists check for key: {Key}", key);
            return Task.FromResult(false);
        }

        Task<bool> IRedisCacheService.IsConnectedAsync()
        {
            _logger.LogWarning("Redis is not available. Skipping connection check.");
            return Task.FromResult(false);
        }
    }
}