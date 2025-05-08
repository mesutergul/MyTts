namespace MyTts.Storage
{
    public class StorageConfiguration
    {
        public string BasePath { get; set; } = string.Empty;
        public string MetadataPath { get; set; } = string.Empty;
        public CacheDurationConfig CacheDuration { get; set; } = new();
        public string DefaultDisk { get; set; } = "local";
        public Dictionary<string, DiskOptions> Disks { get; set; } = new();
    }

    public class CacheDurationConfig
    {
        public TimeSpan Database { get; set; }
        public TimeSpan Files { get; set; }
    }
    public class DiskOptions
    {
        public string Driver { get; set; } = string.Empty;
        public string Root { get; set; } = string.Empty;
        public DiskConfig? Config { get; set; }
        public bool Enabled { get; set; } = true;
    }
    public class DiskConfig
    {
        public string BucketName { get; set; }
        public string AuthJson { get; set; }
        public string DefaultLanguage { get; set; }  // only used by Firebase
        public int? MaxRetries { get; set; }         // nullable: only used by Firebase
        public int? TimeoutSeconds { get; set; }     // nullable: only used by Firebase
    }
    
}
