namespace MyTts.Storage
{
    public class StorageConfiguration
    {
        public string BasePath { get; set; } = string.Empty;
        public string MetadataPath { get; set; } = string.Empty;
        public CacheDurationConfig CacheDuration { get; set; } = new();
        public string DefaultDisk { get; set; } = "local";
        public Dictionary<string, DiskOptions> Disks { get; set; } = new();
        public int BufferSize { get; set; } = 81920;
        public int MaxConcurrentOperations { get; set; } = 10;
        public bool UseMemoryStreamForSmallFiles { get; set; } = true;
        public long SmallFileSizeThreshold { get; set; } = 5 * 1024 * 1024; // 5MB
    }

    public class CacheDurationConfig
    {
        public TimeSpan Database { get; set; } = TimeSpan.FromMinutes(45);
        public TimeSpan Files { get; set; } = TimeSpan.FromMinutes(60);
    }
    public class DiskOptions
    {
        public string Driver { get; set; } = string.Empty;
        public string Root { get; set; } = string.Empty;
        public DiskConfig? Config { get; set; }
        public bool Enabled { get; set; } = true;
        public int BufferSize { get; set; } = 81920;
        public int MaxConcurrentOperations { get; set; } = 10;
    }
    public class DiskConfig
    {
        public string BucketName { get; set; } = string.Empty;
        public string AuthJson { get; set; } = string.Empty;
        public string DefaultLanguage { get; set; } = string.Empty;
        public int MaxRetries { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 60;
        public int BufferSize { get; set; } = 81920;
    }
    
}
