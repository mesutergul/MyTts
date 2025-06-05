using Microsoft.Extensions.Options;
using MyTts.Storage;
using MyTts.Storage.Models;
using MyTts.Storage.Interfaces;
using MyTts.Helpers;
using MyTts.Config.ServiceConfigurations;
using Polly;

namespace MyTts.Config.ServiceConfigurations;

public static class StorageServiceConfig
{
    public static IServiceCollection AddStorageServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure storage with proper validation
        services.AddOptions<StorageConfiguration>()
            .Bind(configuration.GetSection("Storage"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Configure LocalStorageOptions with validation
        services.Configure<LocalStorageOptions>(options =>
        {
            var storageSection = configuration.GetSection("Storage");
            options.BasePath = storageSection["BasePath"] ?? string.Empty;
            options.BufferSize = storageSection.GetValue<int>("BufferSize", 128 * 1024);
            options.MaxConcurrentOperations = storageSection.GetValue<int>("MaxConcurrentOperations", 30);
            options.MaxRetries = storageSection.GetValue<int>("MaxRetries", 3);
            options.RetryDelay = TimeSpan.FromSeconds(storageSection.GetValue<double>("RetryDelaySeconds", 1));
            options.EnableMetrics = storageSection.GetValue<bool>("EnableMetrics", true);

            // Validate options
            if (string.IsNullOrEmpty(options.BasePath))
            {
                throw new InvalidOperationException("Storage base path is required");
            }
            if (options.BufferSize < 4096)
            {
                throw new InvalidOperationException("Buffer size must be at least 4KB");
            }
            if (options.MaxConcurrentOperations < 1)
            {
                throw new InvalidOperationException("Max concurrent operations must be at least 1");
            }
        });

        // Configure CombinedRateLimiter for storage operations
        services.AddSingleton<CombinedRateLimiter>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LocalStorageOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<CombinedRateLimiter>>();
            
            // Calculate requests per second based on max concurrent operations
            // Assuming we want to allow burst up to max concurrent operations
            double requestsPerSecond = options.MaxConcurrentOperations;
            
            return new CombinedRateLimiter(
                maxConcurrentRequests: options.MaxConcurrentOperations,
                requestsPerSecond: requestsPerSecond,
                queueSize: options.MaxConcurrentOperations * 2, // Allow queue size to be double the concurrent operations
                logger: logger
            );
        });

        // Get the configuration instance for initialization
        var storageConfig = configuration.GetSection("Storage").Get<StorageConfiguration>()
            ?? throw new InvalidOperationException("Storage configuration is missing");

        // Validate and initialize storage
        ValidateAndInitializeStorage(storageConfig);

        // Register the configuration as singleton for direct injection
        services.AddSingleton(storageConfig);

        // Register storage services
        RegisterStorageServices(services);

        // Register disk configurations with validation
        services.AddSingleton(sp =>
        {
            var disks = new Dictionary<string, DiskOptions>();
            var logger = sp.GetRequiredService<ILogger<LocalStorageClient>>();

            foreach (var disk in storageConfig.Disks)
            {
                if (!disk.Value.Enabled) continue;

                try
                {
                    // Validate disk configuration
                    if (string.IsNullOrEmpty(disk.Value.Driver))
                    {
                        logger.LogWarning("Disk {DiskName} has no driver specified, skipping", disk.Key);
                        continue;
                    }

                    if (string.IsNullOrEmpty(disk.Value.Root))
                    {
                        logger.LogWarning("Disk {DiskName} has no root path specified, skipping", disk.Key);
                        continue;
                    }

                    // Create a new instance to avoid modifying the original config
                    disks[disk.Key] = new DiskOptions
                    {
                        Driver = disk.Value.Driver,
                        Root = disk.Value.GetNormalizedRoot(),
                        Config = disk.Value.Config ?? new DiskConfig(),
                        Enabled = true,
                        BufferSize = disk.Value.BufferSize,
                        MaxConcurrentOperations = disk.Value.MaxConcurrentOperations
                    };

                    // Ensure disk root directory exists
                    var rootPath = disk.Value.GetNormalizedRoot();
                    if (!Directory.Exists(rootPath))
                    {
                        Directory.CreateDirectory(rootPath);
                        logger.LogInformation("Created directory for disk {DiskName}: {Path}", disk.Key, rootPath);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to configure disk {DiskName}", disk.Key);
                }
            }

            return disks;
        });

        return services;
    }

    private static void RegisterStorageServices(IServiceCollection services)
    {
        // Register the LocalStorageClient as primary implementation
        services.AddScoped<ILocalStorageClient, LocalStorageClient>();
    }

    private static void ValidateAndInitializeStorage(StorageConfiguration config)
    {
        // Validate the configuration
        config.Validate();

        try
        {
            // Create necessary directories
            var basePath = config.GetNormalizedBasePath();
            var metadataPath = Path.GetDirectoryName(config.GetNormalizedMetadataPath())!;

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            if (!Directory.Exists(metadataPath))
            {
                Directory.CreateDirectory(metadataPath);
            }

            // Initialize the static helper
            StoragePathHelper.Initialize(config);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initialize storage directories", ex);
        }
    }
} 