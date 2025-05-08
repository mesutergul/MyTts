namespace MyTts.Services
{
    public static class SemaphoreExtensions
    {
        public static async Task<IAsyncDisposable> WaitAsyncDisposable(
            this SemaphoreSlim semaphore,
            CancellationToken cancellationToken = default)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new SemaphoreReleaser(semaphore);
        }

        private sealed class SemaphoreReleaser : IAsyncDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private int _released;

            public SemaphoreReleaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public ValueTask DisposeAsync()
            {
                // Use Interlocked to ensure thread-safe release
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    _semaphore.Release();
                }
                return ValueTask.CompletedTask;
            }
        }
    }
}