using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
namespace MyTts.Helpers
{
    public class CombinedRateLimiter
    {
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly TokenBucketRateLimiter _timeBasedLimiter;
        public CombinedRateLimiter(int maxConcurrentRequests, double requestsPerSecond)
        {
            if (maxConcurrentRequests <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentRequests), "maxConcurrentRequests must be a positive integer.");
            }
            if (requestsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestsPerSecond), "requestsPerSecond must be a positive float.");
            }
            _concurrencyLimiter = new SemaphoreSlim(maxConcurrentRequests);
            _timeBasedLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst, // Process oldest requests first
                TokenLimit = maxConcurrentRequests, // This is the burst capacity; it allows an initial burst of requests
                QueueLimit = 0, // No internal queue; SemaphoreSlim handles queuing
                ReplenishmentPeriod = TimeSpan.FromSeconds(1), // Tokens are replenished every second
                TokensPerPeriod = (int)Math.Ceiling(requestsPerSecond), // Ensure we replenish at least the desired rate
                AutoReplenishment = true // Automatically replenish tokens
            });

            Console.WriteLine("CombinedRateLimiter initialized:");
            Console.WriteLine($"  Max Concurrent Requests: {maxConcurrentRequests}");
            Console.WriteLine($"  Requests Per Second: {requestsPerSecond}");
        }
        public async Task AcquireAsync()
        {
            await _concurrencyLimiter.WaitAsync();
            RateLimitLease lease = await _timeBasedLimiter.AcquireAsync(1);
            if (!lease.IsAcquired)
            {
                throw new InvalidOperationException("Failed to acquire time-based rate limit lease.");
            }
        }
        public void Release()
        {
            _concurrencyLimiter.Release();
        }
  }
}
