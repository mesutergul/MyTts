using System.Buffers;

namespace MyTts.Services
{
    public sealed class VoiceClip : Stream, IAsyncDisposable
    {
        private readonly MemoryStream _audioDataStream;
        private bool _disposed;

        public VoiceClip(byte[] audioData)
        {
            ArgumentNullException.ThrowIfNull(audioData);
            _audioDataStream = new MemoryStream(audioData, writable: false); // Read-only for safety
        }

        public VoiceClip(Memory<byte> audioData)
        {
            _audioDataStream = new MemoryStream(audioData.ToArray(), writable: false);
        }

        // Stream implementation
        public override bool CanRead => !_disposed && _audioDataStream.CanRead;
        public override bool CanSeek => !_disposed && _audioDataStream.CanSeek;
        public override bool CanWrite => false; // Make it read-only
        public override long Length => _disposed ? 0 : _audioDataStream.Length;
        public override long Position
        {
            get => _disposed ? 0 : _audioDataStream.Position;
            set
            {
                ThrowIfDisposed();
                _audioDataStream.Position = value;
            }
        }

        // Optimized methods for performance
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return await _audioDataStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await _audioDataStream.ReadAsync(buffer, cancellationToken);
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize = 81920, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(destination);

            // Use ArrayPool for buffer management
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int bytesRead;
                while ((bytesRead = await ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // Make stream shareable for merging operations
        public Stream GetShareableStream()
        {
            ThrowIfDisposed();
            _audioDataStream.Position = 0;
            return new MemoryStream(_audioDataStream.ToArray(), writable: false);
        }

        // Basic Stream implementation
        public override void Flush() => ThrowIfDisposed();
        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            return _audioDataStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            return _audioDataStream.Seek(offset, origin);
        }

        public override void SetLength(long value) => throw new NotSupportedException("Stream is read-only");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Stream is read-only");

        // Disposal
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _audioDataStream.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await _audioDataStream.DisposeAsync();
                _disposed = true;
            }
            await base.DisposeAsync();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VoiceClip));
            }
        }

        // Conversion operator
        public static implicit operator VoiceClip(ElevenLabs.VoiceClip voiceClip)
        {
            ArgumentNullException.ThrowIfNull(voiceClip);

            try
            {
                return new VoiceClip(voiceClip.ClipData.ToArray());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to convert ElevenLabs.VoiceClip to VoiceClip", ex);
            }
        }
    }
}
