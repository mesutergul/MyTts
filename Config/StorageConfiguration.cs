using System.ComponentModel.DataAnnotations;

namespace MyTts.Storage
{
    public class StorageConfiguration
    {
        [Required(ErrorMessage = "BasePath is required")]
        public string BasePath { get; set; } = string.Empty;

        [Required(ErrorMessage = "MetadataPath is required")]
        public string MetadataPath { get; set; } = string.Empty;

        public CacheDurationConfig CacheDuration { get; set; } = new();

        [Required(ErrorMessage = "DefaultDisk is required")]
        public string DefaultDisk { get; set; } = "local";

        public Dictionary<string, DiskOptions> Disks { get; set; } = new()
        {
            { "local", new DiskOptions { Driver = "local", Root = string.Empty } }
        };

        [Range(4096, int.MaxValue, ErrorMessage = "BufferSize must be at least 4KB")]
        public int BufferSize { get; set; } = 81920;

        [Range(1, 100, ErrorMessage = "MaxConcurrentOperations must be between 1 and 100")]
        public int MaxConcurrentOperations { get; set; } = 10;

        public bool UseMemoryStreamForSmallFiles { get; set; } = true;

        [Range(1024, long.MaxValue, ErrorMessage = "SmallFileSizeThreshold must be at least 1KB")]
        public long SmallFileSizeThreshold { get; set; } = 5 * 1024 * 1024; // 5MB

        public string GetNormalizedBasePath() => Path.GetFullPath(BasePath);
        public string GetNormalizedMetadataPath() => Path.GetFullPath(MetadataPath);

        public DiskOptions GetDiskOptions(string? diskName = null)
        {
            diskName ??= DefaultDisk;
            return Disks.TryGetValue(diskName, out var disk) 
                ? disk 
                : throw new KeyNotFoundException($"Disk '{diskName}' not found in configuration");
        }

        public void Validate()
        {
            if (string.IsNullOrEmpty(BasePath))
                throw new ValidationException("BasePath is required");

            if (string.IsNullOrEmpty(MetadataPath))
                throw new ValidationException("MetadataPath is required");

            if (string.IsNullOrEmpty(DefaultDisk))
                throw new ValidationException("DefaultDisk is required");

            if (!Disks.ContainsKey(DefaultDisk))
                throw new ValidationException($"Default disk '{DefaultDisk}' is not configured in Disks");

            foreach (var disk in Disks.Values)
                disk.Validate();
        }
    }

    public class CacheDurationConfig
    {
        [Range(typeof(TimeSpan), "00:01:00", "01:00:00", ErrorMessage = "Database cache duration must be between 1 and 60 minutes")]
        public TimeSpan Database { get; set; } = TimeSpan.FromMinutes(45);

        [Range(typeof(TimeSpan), "00:01:00", "02:00:00", ErrorMessage = "File cache duration must be between 1 and 120 minutes")]
        public TimeSpan Files { get; set; } = TimeSpan.FromMinutes(60);

        public void Validate()
        {
            if (Database < TimeSpan.FromMinutes(1) || Database > TimeSpan.FromMinutes(60))
                throw new ValidationException("Database cache duration must be between 1 and 60 minutes");

            if (Files < TimeSpan.FromMinutes(1) || Files > TimeSpan.FromMinutes(120))
                throw new ValidationException("File cache duration must be between 1 and 120 minutes");
        }
    }

    public class DiskOptions
    {
        [Required(ErrorMessage = "Driver is required")]
        public string Driver { get; set; } = string.Empty;

        [Required(ErrorMessage = "Root is required")]
        public string Root { get; set; } = string.Empty;

        public DiskConfig? Config { get; set; }

        public bool Enabled { get; set; } = true;

        [Range(4096, int.MaxValue, ErrorMessage = "BufferSize must be at least 4KB")]
        public int BufferSize { get; set; } = 81920;

        [Range(1, 100, ErrorMessage = "MaxConcurrentOperations must be between 1 and 100")]
        public int MaxConcurrentOperations { get; set; } = 10;

        public string GetNormalizedRoot() => Path.GetFullPath(Root);

        public void Validate()
        {
            if (string.IsNullOrEmpty(Driver))
                throw new ValidationException("Driver is required");

            if (string.IsNullOrEmpty(Root))
                throw new ValidationException("Root is required");

            if (BufferSize < 4096)
                throw new ValidationException("BufferSize must be at least 4KB");

            if (MaxConcurrentOperations < 1 || MaxConcurrentOperations > 100)
                throw new ValidationException("MaxConcurrentOperations must be between 1 and 100");

            Config?.Validate();
        }
    }

    public class DiskConfig
    {
        public string BucketName { get; set; } = string.Empty;
        public string AuthJson { get; set; } = string.Empty;
        public string DefaultLanguage { get; set; } = string.Empty;

        [Range(1, 10, ErrorMessage = "MaxRetries must be between 1 and 10")]
        public int MaxRetries { get; set; } = 3;

        [Range(1, 300, ErrorMessage = "TimeoutSeconds must be between 1 and 300")]
        public int TimeoutSeconds { get; set; } = 60;

        [Range(4096, int.MaxValue, ErrorMessage = "BufferSize must be at least 4KB")]
        public int BufferSize { get; set; } = 81920;

        public void Validate()
        {
            if (MaxRetries < 1 || MaxRetries > 10)
                throw new ValidationException("MaxRetries must be between 1 and 10");

            if (TimeoutSeconds < 1 || TimeoutSeconds > 300)
                throw new ValidationException("TimeoutSeconds must be between 1 and 300");

            if (BufferSize < 4096)
                throw new ValidationException("BufferSize must be at least 4KB");
        }
    }
}
