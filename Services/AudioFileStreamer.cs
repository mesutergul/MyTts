using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace MyTts.Services
{
    public static class AudioFileStreamer
    {
        /// <summary>
        /// Streams an audio file asynchronously, allowing processing in chunks.
        /// </summary>
        /// <param name="filePath">The path to the audio file.</param>
        /// <param name="processChunkAsync">An asynchronous function to process each chunk of data.</param>
        /// <param name="bufferSize">The size of the buffer to use for reading chunks (e.g., 81920 bytes for 80KB).</param>
        /// <param name="cancellationToken">A CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
        public static async Task StreamFileInChunksAsync(
            string filePath,
            Func<byte[], int, int, CancellationToken, Task> processChunkAsync,
            int bufferSize = 81920, // Common buffer size for efficient I/O (multiples of 4KB or 8KB)
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Audio file not found at: {filePath}");
            }

            // Using FileStream for efficient, chunked reading.
            // FileOptions.Asynchronous enables true async I/O for better performance.
            using (var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read, // Allow other processes to read the file concurrently
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan)) // SequentialScan optimizes for forward-only reading
            {
                var buffer = new byte[bufferSize];
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Process the chunk.
                    // Pass the actual number of bytes read to avoid processing empty buffer parts.
                    await processChunkAsync(buffer, 0, bytesRead, cancellationToken);
                }
            }
        }

        // Example usage if you want to simply copy to another stream:
        public static async Task CopyFileToStreamAsync(string sourceFilePath, Stream destinationStream, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException($"Audio file not found at: {sourceFilePath}");
            }

            using (var fileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await fileStream.CopyToAsync(destinationStream, 81920, cancellationToken);
            }
        }
    }
}