using System.Buffers; // Required for MemoryPool and IMemoryOwner
namespace MyTts.Services
{
    public class AdvancedAudioFileReader
    {
        private readonly ILogger<AdvancedAudioFileReader> _logger;
        private const int DefaultBufferSize = 81920; // A common efficient buffer size (e.g., 80KB)

        public AdvancedAudioFileReader(ILogger<AdvancedAudioFileReader> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Streams an audio file in chunks using buffers rented from MemoryPool.Shared.
        /// This reduces memory allocations and GC pressure by reusing buffers.
        /// </summary>
        /// <param name="filePath">The path to the audio file.</param>
        /// <param name="processChunkAsync">An asynchronous function to process each chunk of data.</param>
        /// <param name="bufferSize">The size of the buffer to rent from the pool. Defaults to 80KB.</param>
        /// <param name="cancellationToken">A CancellationToken to observe while waiting for the task to complete.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist.</exception>
        /// <exception cref="OperationCanceledException">Thrown if the operation is cancelled.</exception>
        /// <exception cref="Exception">Thrown for other unexpected errors during streaming.</exception>
        public async Task StreamFileUsingMemoryPoolAsync(
            string filePath,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task> processChunkAsync,
            int bufferSize = DefaultBufferSize,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError("Audio file not found at: {FilePath}", filePath);
                throw new FileNotFoundException($"Audio file not found at: {filePath}");
            }

            // 1. Rent a buffer from the shared MemoryPool.
            // The 'using' statement ensures that memoryOwner.Dispose() is called,
            // which returns the rented memory to the pool.
            using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(bufferSize);
            Memory<byte> buffer = memoryOwner.Memory; // Get the Memory<byte> slice representing the rented buffer

            try
            {
                // 2. Open the file stream with asynchronous options.
                using (var fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read, // Allow other processes to read the file concurrently
                    bufferSize, // Pass bufferSize to FileStream for internal buffer optimization
                    FileOptions.Asynchronous | FileOptions.SequentialScan)) // Asynchronous enables true async I/O; SequentialScan optimizes for forward-only reading
                {
                    int bytesRead;
                    // 3. Read chunks using the ReadAsync overload that takes Memory<byte>.
                    while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // 4. Get a slice of the buffer that contains only the actually read bytes.
                        // This is crucial to avoid processing uninitialized or stale data.
                        ReadOnlyMemory<byte> chunk = buffer.Slice(0, bytesRead);

                        // 5. Process the chunk of data using the provided delegate.
                        await processChunkAsync(chunk, cancellationToken);
                    }
                }
                _logger.LogInformation("Successfully streamed file {FilePath} using MemoryPool.", filePath);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "File streaming was cancelled for {FilePath}.", filePath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while streaming file {FilePath} using MemoryPool.", filePath);
                throw;
            }
            // The 'using' statement on 'memoryOwner' ensures memory is returned to the pool automatically.
        }

        // --- Example of how to implement a 'processChunkAsync' delegate ---
        // This example simply copies the chunks to another stream.
        public async Task CopyFileToStreamUsingMemoryPoolAsync(
            string sourceFilePath,
            Stream destinationStream,
            int bufferSize = DefaultBufferSize,
            CancellationToken cancellationToken = default)
        {
            if (destinationStream == null)
            {
                throw new ArgumentNullException(nameof(destinationStream));
            }

            // Use the general streaming method
            await StreamFileUsingMemoryPoolAsync(
                sourceFilePath,
                async (chunk, ct) =>
                {
                    // Use the WriteAsync overload that takes ReadOnlyMemory<byte> for efficiency
                    await destinationStream.WriteAsync(chunk, ct);
                },
                bufferSize,
                cancellationToken
            );
            _logger.LogInformation("Successfully copied file {SourceFilePath} to destination stream using MemoryPool.", sourceFilePath);
        }

        // Another example: a simple processor that just logs the chunk size (for demonstration)
        public static async Task LogChunkSizeProcessor(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken)
        {
            // In a real application, you would perform your audio processing here.
            // For instance, decode the audio, analyze it, or send it over a network.
            // await SomeAudioDecoder.DecodeChunkAsync(chunk, cancellationToken);
            // Console.WriteLine($"Processed chunk of {chunk.Length} bytes.");
            await Task.Delay(1, cancellationToken); // Simulate some non-blocking work
        }
        // Example usage (assuming you have an ILogger instance)
        // using Microsoft.Extensions.Logging;
        // var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        // var logger = loggerFactory.CreateLogger<AdvancedAudioFileReader>();
        // var reader = new AdvancedAudioFileReader(logger);

        // string audioFilePath = "path/to/your/audio.mp3"; // Replace with your audio file path
        // string outputFilePath = "path/to/your/output.m4a"; // For copying example

        // try
        // {
        //     // Example 1: Stream and process chunks (e.g., analyze, decode)
        //     Console.WriteLine($"Streaming '{audioFilePath}' and processing chunks...");
        //     await reader.StreamFileUsingMemoryPoolAsync(
        //         audioFilePath,
        //         AdvancedAudioFileReader.LogChunkSizeProcessor // Your custom chunk processing logic
        //     );
        //     Console.WriteLine("Streaming completed.");

        //     // Example 2: Copy file content to another stream (e.g., saving to a new file)
        //     Console.WriteLine($"Copying '{audioFilePath}' to '{outputFilePath}'...");
        //     using (var outputStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        //     {
        //         await reader.CopyFileToStreamUsingMemoryPoolAsync(audioFilePath, outputStream);
        //     }
        //     Console.WriteLine("File copy completed.");
        // }
        // catch (FileNotFoundException ex)
        // {
        //     Console.WriteLine($"Error: {ex.Message}");
        // }
        // catch (OperationCanceledException)
        // {
        //     Console.WriteLine("Operation cancelled.");
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"An unexpected error occurred: {ex.Message}");
        // }
    }
}