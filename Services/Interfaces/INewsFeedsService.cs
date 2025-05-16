namespace MyTts.Services.Interfaces
{

    /// <summary>
    /// Service for fetching and processing news feeds from various sources
    /// </summary>
    public interface INewsFeedsService
    {
        /// <summary>
        /// Gets the feed URL for a specific language
        /// </summary>
        string GetFeedUrl(string language);

        /// <summary>
        /// Fetches contents from an external service
        /// </summary>
        Task<List<string>> FetchContentsFromExternalServiceAsync(string language, int limit);

        /// <summary>
        /// Gets processed feed data for a specific language
        /// </summary>
        Task<List<string>> GetFeedByLanguageAsync(string language, int limit);
    }
}