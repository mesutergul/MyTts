namespace MyTts.Services
{
    public sealed class AudioProcessor : IAsyncDisposable
    {
        private readonly VoiceClip _voiceClip;
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public AudioProcessor(VoiceClip voiceClip)
        {
            _voiceClip = voiceClip ?? throw new ArgumentNullException(nameof(voiceClip));
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioProcessor));
            
            ArgumentNullException.ThrowIfNull(destination);
            
            // Using ValueTask-based pattern for more efficient async operations
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Reset position first
                _voiceClip.Position = 0;
                
                // Use optimal buffer size based on expected audio sizes
                // Using 32KB which is good for audio streaming without excessive memory use
                await _voiceClip.CopyToAsync(destination, 128*1024, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // Optimized method for cloud uploads that doesn't release the semaphore
        // Returns a stream reference without creating a new copy
        public async ValueTask<Stream> GetStreamForCloudUploadAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioProcessor));
            
            bool acquired = false;
            try
            {
                acquired = await _semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false);
                
                // If we couldn't acquire immediately, try with wait
                if (!acquired)
                {
                    await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    acquired = true;
                }
                
                _voiceClip.Position = 0;
                
                // Create a wrapper stream that will release the semaphore when disposed
                return new SemaphoreReleasingStream(_voiceClip, _semaphore);
            }
            catch
            {
                if (acquired)
                    _semaphore.Release();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await _voiceClip.DisposeAsync().ConfigureAwait(false);
                _semaphore.Dispose();
                _disposed = true;
            }
        }

        // Helper class to ensure semaphore is released when stream is disposed
        private class SemaphoreReleasingStream : Stream
        {
            private readonly Stream _innerStream;
            private readonly SemaphoreSlim _semaphore;
            private bool _disposed;

            public SemaphoreReleasingStream(Stream innerStream, SemaphoreSlim semaphore)
            {
                _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
                _semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
            }

            public override bool CanRead => !_disposed && _innerStream.CanRead;
            public override bool CanSeek => !_disposed && _innerStream.CanSeek;
            public override bool CanWrite => !_disposed && _innerStream.CanWrite;
            public override long Length => _disposed ? 0 : _innerStream.Length;
            
            public override long Position
            {
                get => _disposed ? 0 : _innerStream.Position;
                set
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(SemaphoreReleasingStream));
                    _innerStream.Position = value;
                }
            }

            public override void Flush()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SemaphoreReleasingStream));
                _innerStream.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SemaphoreReleasingStream));
                return _innerStream.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SemaphoreReleasingStream));
                return _innerStream.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SemaphoreReleasingStream));
                _innerStream.SetLength(value);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SemaphoreReleasingStream));
                _innerStream.Write(buffer, offset, count);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SemaphoreReleasingStream));
                return _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SemaphoreReleasingStream));
                return _innerStream.ReadAsync(buffer, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                if (!_disposed)
                {
                    if (disposing)
                    {
                        // Don't dispose the inner stream - only release the semaphore
                        _semaphore.Release();
                    }
                    _disposed = true;
                }
                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                if (!_disposed)
                {
                    // Don't dispose the inner stream - only release the semaphore
                    _semaphore.Release();
                    _disposed = true;
                }
                await base.DisposeAsync();
            }
        }
    }
}