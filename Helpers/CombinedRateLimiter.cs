using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

namespace MyTts.Helpers
{
    public class CombinedRateLimiter: IAsyncDisposable
    {
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly TokenBucketRateLimiter _timeBasedLimiter;
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
        private readonly ILogger<CombinedRateLimiter>? _logger;
        private bool _disposed;

        public CombinedRateLimiter(int maxConcurrentRequests, double requestsPerSecond, int queueSize = 10, ILogger<CombinedRateLimiter>? logger = null)
        {
            _logger = logger;
            if (maxConcurrentRequests <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentRequests), "maxConcurrentRequests must be a positive integer.");
            }
            if (requestsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestsPerSecond), "requestsPerSecond must be a positive float.");
            }
            if (queueSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(queueSize), "queueSize must be a non-negative integer.");
            }

            _concurrencyLimiter = new SemaphoreSlim(maxConcurrentRequests);
            _timeBasedLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst, // Process oldest requests first
                TokenLimit = maxConcurrentRequests, // This is the burst capacity; it allows an initial burst of requests
                QueueLimit = queueSize, // Allow queuing of requests when rate limit is exceeded
                ReplenishmentPeriod = TimeSpan.FromSeconds(1), // Tokens are replenished every second
                TokensPerPeriod = (int)Math.Ceiling(requestsPerSecond), // Ensure we replenish at least the desired rate
                AutoReplenishment = true // Automatically replenish tokens
            });

            Console.WriteLine("CombinedRateLimiter initialized:");
            Console.WriteLine($"  Max Concurrent Requests: {maxConcurrentRequests}");
            Console.WriteLine($"  Requests Per Second: {requestsPerSecond}");
            Console.WriteLine($"  Queue Size: {queueSize}");
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
            try 
            {
                RateLimitLease lease = await _timeBasedLimiter.AcquireAsync(1, cancellationToken);
                if (!lease.IsAcquired)
                {
                    _logger?.LogWarning("Rate limit exceeded, request queued");
                    // Wait for the lease to be acquired
                    lease = await _timeBasedLimiter.AcquireAsync(1, cancellationToken);
                    if (!lease.IsAcquired)
                    {
                        _concurrencyLimiter.Release();
                        throw new InvalidOperationException("Failed to acquire time-based rate limit lease after queuing.");
                    }
                }
            }
            catch (Exception)
            {
                _concurrencyLimiter.Release();
                throw;
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
