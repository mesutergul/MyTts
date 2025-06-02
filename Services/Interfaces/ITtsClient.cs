using MyTts.Models;
using ElevenLabs.TextToSpeech;

namespace MyTts.Services.Interfaces
{
    public interface ITtsClient : IAsyncDisposable
    {
        /// <summary>
        /// Processes content and generates audio file
        /// </summary>
        Task<(int id, AudioProcessor FileData)> ProcessContentAsync(
            string text,
            int id,
            string language,
            AudioType fileType,
            CancellationToken cancellationToken);

        /// <summary>
        /// Processes previously saved content
        /// </summary>
        Task<(int id, AudioProcessor Processor)> ProcessSavedContentAsync(
            int ilgiId,
            AudioType fileType,
            CancellationToken cancellationToken);

        /// <summary>
        /// Processes multiple contents and merges them into a single audio file
        /// </summary>
        Task<string> ProcessContentsAsync(
            IEnumerable<HaberSummaryDto> allNews,
            IEnumerable<HaberSummaryDto> neededNews,
            IEnumerable<HaberSummaryDto> savedNews,
            string language,
            AudioType fileType,
            CancellationToken cancellationToken = default);
 
    }
} 