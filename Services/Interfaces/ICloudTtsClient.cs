using MyTts.Models;
using System.Threading.Tasks;
using System.IO;

namespace MyTts.Services.Interfaces
{
    public interface ICloudTtsClient : IAsyncDisposable
    {
        /// <summary>
        /// Generates audio from text using Gemini AI and returns it as a stream.
        /// </summary>
        /// <param name="text">The text to synthesize.</param>
        /// <param name="languageCode">The language code (e.g., "en-US").</param>
        /// <param name="voiceName">The specific voice name to use (if applicable to Gemini API).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A stream containing the audio data.</returns>
        Task<byte[]> SynthesizeSpeechAsync(
            string text,
            string languageCode,
            string voiceName, // Or other relevant parameters like gender, speaking rate
            CancellationToken cancellationToken);

        // Add other methods if needed, for example, to list available voices
        // Task<IEnumerable<string>> ListVoicesAsync(string languageCode, CancellationToken cancellationToken);
    }
}