using MyTts.Services.Interfaces;

namespace MyTts.Services
{
    public class NullRedisCacheService : IRedisCacheService
    {
        private readonly ILogger<NullRedisCacheService> _logger;

        public NullRedisCacheService(ILogger<NullRedisCacheService> logger)
        {
            _logger = logger;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        {
            _logger.LogWarning("Redis is not available. Returning default value for key: {Key}", key);
            return Task.FromResult<T?>(default);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("Redis is not available. Skipping cache set for key: {Key}", key);
            return Task.CompletedTask;
        }

        Task<bool> IRedisCacheService.RemoveAsync(string key, CancellationToken cancellationToken)
        {
            _logger.LogWarning("Redis is not available. Skipping cache remove for key: {Key}", key);
            return Task.FromResult(false);
        }

        Task<bool> IRedisCacheService.ExistsAsync(string key, CancellationToken cancellationToken)
        {
            _logger.LogWarning("Redis is not available. Skipping exists check for key: {Key}", key);
            return Task.FromResult(false);
        }

        Task<bool> IRedisCacheService.IsConnectedAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning("Redis is not available. Skipping connection check.");
            return Task.FromResult(false);
        }

        Task IRedisCacheService.SetBytesAsync(string key, byte[] value, TimeSpan? expiry, CancellationToken cancellationToken)
        {
            _logger.LogWarning("Redis is not available. Skipping cache set for key: {Key}", key);
            return Task.CompletedTask;
        }

        Task<byte[]?> IRedisCacheService.GetBytesAsync(string key, CancellationToken cancellationToken)
        {
            _logger.LogWarning("Redis is not available. Returning default value for key: {Key}", key);
            return Task.FromResult<byte[]?>(default);
        }
    }
}