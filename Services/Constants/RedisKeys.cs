namespace MyTts.Services.Constants
{
    public static class RedisKeys
    {
        // TTS related keys
        public const string TTS_KEY_PREFIX = "tts";
        public const string TTS_METADATA_KEY = "tts:{0}"; // Format with ID

        // MP3 related keys
        public const string MP3_DB_KEY = "mp3:db";
        public const string MP3_FILE_KEY = "mp3:file:{0}"; // Format with ID
        public const string MP3_MERGED_KEY = "mp3:merged:{0}"; // Format with ID
        public const string MP3_META_KEY = "mp3:meta:{0}"; // Format with ID
        public const string MP3_METADATA_DB_KEY = "mp3:metadata:db"; // For metadata database

        // Cache durations
        public static readonly TimeSpan DEFAULT_METADATA_EXPIRY = TimeSpan.FromHours(1);
        public static readonly TimeSpan DB_CACHE_DURATION = TimeSpan.FromMinutes(45);
        public static readonly TimeSpan FILE_CACHE_DURATION = TimeSpan.FromMinutes(60);

        // Helper method to format keys
        public static string FormatKey(string pattern, params object[] args)
        {
            return string.Format(pattern, args);
        }
    }
} 