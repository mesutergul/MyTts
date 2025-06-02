using System.Threading.RateLimiting;
namespace MyTts.Helpers
{
    public class CombinedRateLimiter: IAsyncDisposable
    {
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly TokenBucketRateLimiter _timeBasedLimiter;
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
        private bool _disposed;

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

        public CancellationToken CreateLinkedTokenWithTimeout(CancellationToken originalToken, TimeSpan? timeout = null)
        {
            ThrowIfDisposed();
            var timeoutCts = new CancellationTokenSource(timeout ?? _defaultTimeout);
            return CancellationTokenSource.CreateLinkedTokenSource(originalToken, timeoutCts.Token).Token;
        }

        public async Task AcquireAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await _concurrencyLimiter.WaitAsync(cancellationToken);
            RateLimitLease lease = await _timeBasedLimiter.AcquireAsync(1, cancellationToken);
            if (!lease.IsAcquired)
            {
                _concurrencyLimiter.Release();
                throw new InvalidOperationException("Failed to acquire time-based rate limit lease.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _concurrencyLimiter.Dispose();
                await _timeBasedLimiter.DisposeAsync();
            }
        }

        public void Release()
        {
            ThrowIfDisposed();
            _concurrencyLimiter.Release();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CombinedRateLimiter));
            }
        }
    }
}
