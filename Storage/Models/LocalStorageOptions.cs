using System;

namespace MyTts.Storage.Models
{
    public class LocalStorageOptions
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public int BufferSize { get; set; } = 128 * 1024; // 128KB default
        public string BasePath { get; set; } = string.Empty;
        public int MaxConcurrentOperations { get; set; } = 30;
        public bool EnableMetrics { get; set; } = true;
    }
} 