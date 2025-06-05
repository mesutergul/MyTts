namespace MyTts.Services.Constants
{
    public static class RedisKeys
    {
        // TTS related keys
        public const string TTS_KEY_PREFIX = "tts";
        public const string TTS_METADATA_KEY = "tts:{0}"; // Format with ID
        public const string TTS_INDIVIDUAL_MP3_KEY = "tts:individual:{0}"; // Format with ID
        public const string TTS_STREAM_KEY = "tts:stream:{0}"; // Format with ID
        public const string TTS_MERGE_KEY = "tts:merge:{0}"; // Format with ID

        // MP3 related keys
        public const string MP3_DB_KEY = "mp3:db";
        public const string MP3_FILE_KEY = "mp3:file:{0}"; // Format with ID
        public const string MP3_MERGED_KEY = "mp3:merged:{0}"; // Format with ID
        public const string MP3_META_KEY = "mp3:meta:{0}"; // Format with ID
        public const string MP3_METADATA_DB_KEY = "mp3:metadata:db"; // For metadata database
        public const string MP3_STREAM_KEY = "mp3:stream:{0}"; // Format with ID
        public const string MP3_DISK_KEY = "mp3:disk:{0}"; // Format with ID

        // Hash related keys
        public const string HASH_KEY = "hash:{0}"; // Format with ID
        public const string HASH_BATCH_KEY = "hash:batch";

        // Feed related keys
        public const string FEED_KEY = "feed:{0}"; // Format with language

        // Cache durations
        public static readonly TimeSpan DEFAULT_METADATA_EXPIRY = TimeSpan.FromHours(1);
        public static readonly TimeSpan DB_CACHE_DURATION = TimeSpan.FromMinutes(45);
        public static readonly TimeSpan FILE_CACHE_DURATION = TimeSpan.FromHours(2);
        public static readonly TimeSpan HASH_CACHE_DURATION = TimeSpan.FromDays(2);
        public static readonly TimeSpan STREAM_CACHE_DURATION = TimeSpan.FromHours(24);
        public static readonly TimeSpan INDIVIDUAL_MP3_DURATION = TimeSpan.FromHours(12);

        // Helper method to format keys
        public static string FormatKey(string pattern, params object[] args)
        {
            return string.Format(pattern, args);
        }
    }
} 