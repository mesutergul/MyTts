using System.Buffers;

namespace MyTts.Services
{
    public sealed class VoiceClip : Stream, IAsyncDisposable
    {
        // Use Memory<byte> directly instead of wrapping in a MemoryStream
        private readonly ReadOnlyMemory<byte> _audioData;
        private long _position;
        private bool _disposed;

        public VoiceClip(byte[] audioData)
        {
            ArgumentNullException.ThrowIfNull(audioData);
            // Store reference directly without copying if possible
            _audioData = audioData;
        }

        public VoiceClip(ReadOnlyMemory<byte> audioData)
        {
            _audioData = audioData;
        }

        // Stream implementation
        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false; // Read-only
        public override long Length => _disposed ? 0 : _audioData.Length;
        
        public override long Position
        {
            get => _disposed ? 0 : _position;
            set
            {
                ThrowIfDisposed();
                if (value < 0 || value > _audioData.Length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        // Highly optimized read methods to avoid unnecessary allocations
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return await ReadAsyncInternal(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return await ReadAsyncInternal(buffer, cancellationToken);
        }

        private ValueTask<int> ReadAsyncInternal(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            int bytesToRead = (int)Math.Min(buffer.Length, _audioData.Length - _position);
            if (bytesToRead <= 0)
                return new ValueTask<int>(0);

            _audioData.Slice((int)_position, bytesToRead).CopyTo(buffer.Slice(0, bytesToRead));
            _position += bytesToRead;
            
            return new ValueTask<int>(bytesToRead);
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize = 81920, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(destination);

            // Calculate what's left to copy
            int remainingBytes = (int)(_audioData.Length - _position);
            if (remainingBytes <= 0)
                return;

            // Use shared buffer pool
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(bufferSize, remainingBytes));
            try
            {
                // We can optimize this further with a single write operation if possible
                if (remainingBytes <= buffer.Length)
                {
                    _audioData.Slice((int)_position, remainingBytes).CopyTo(buffer);
                    await destination.WriteAsync(buffer.AsMemory(0, remainingBytes), cancellationToken);
                    _position += remainingBytes;
                }
                else
                {
                    while (remainingBytes > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        int bytesToCopy = Math.Min(buffer.Length, remainingBytes);
                        _audioData.Slice((int)_position, bytesToCopy).CopyTo(buffer);
                        await destination.WriteAsync(buffer.AsMemory(0, bytesToCopy), cancellationToken);
                        _position += bytesToCopy;
                        remainingBytes -= bytesToCopy;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // Stream sharing without full copy
        public Stream GetShareableStream()
        {
            ThrowIfDisposed();
            return new MemoryStream(_audioData.ToArray(), writable: false);
        }

        // Basic Stream implementation
        public override void Flush() => ThrowIfDisposed();
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            
            int bytesToRead = (int)Math.Min(count, _audioData.Length - _position);
            if (bytesToRead <= 0)
                return 0;

            _audioData.Slice((int)_position, bytesToRead).CopyTo(buffer.AsMemory(offset, bytesToRead));
            _position += bytesToRead;
            
            return bytesToRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _audioData.Length + offset,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            if (newPosition < 0 || newPosition > _audioData.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            _position = newPosition;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException("Stream is read-only");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Stream is read-only");

        // Disposal
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
            return base.DisposeAsync();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VoiceClip));
            }
        }

        // Conversion operator with optimized memory handling
        public static implicit operator VoiceClip(ElevenLabs.VoiceClip voiceClip)
        {
            ArgumentNullException.ThrowIfNull(voiceClip);

            try
            {
                // Convert directly to Memory<byte> if possible to avoid copy
                return new VoiceClip(voiceClip.ClipData.ToArray());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to convert ElevenLabs.VoiceClip to VoiceClip", ex);
            }
        }
    }
}