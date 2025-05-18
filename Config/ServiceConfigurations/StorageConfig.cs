using Microsoft.Extensions.Options;
using MyTts.Storage;
using MyTts.Storage.Models;
using MyTts.Storage.Interfaces;
using MyTts.Helpers;

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

        // Configure LocalStorageOptions
        services.Configure<LocalStorageOptions>(options =>
        {
            var storageSection = configuration.GetSection("Storage");
            options.BasePath = storageSection["BasePath"] ?? string.Empty;
            options.BufferSize = storageSection.GetValue<int>("BufferSize", 128 * 1024);
            options.MaxConcurrentOperations = storageSection.GetValue<int>("MaxConcurrentOperations", 10);
            options.MaxRetries = storageSection.GetValue<int>("MaxRetries", 3);
            options.RetryDelay = TimeSpan.FromSeconds(storageSection.GetValue<double>("RetryDelaySeconds", 1));
            options.EnableMetrics = storageSection.GetValue<bool>("EnableMetrics", true);
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

        // Register disk configurations 
        services.AddSingleton(sp =>
        {
            var disks = new Dictionary<string, DiskOptions>();

            foreach (var disk in storageConfig.Disks)
            {
                if (!disk.Value.Enabled) continue;

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
            }

            return disks;
        });

        return services;
    }

    private static void RegisterStorageServices(IServiceCollection services)
    {
        // Register the LocalStorageClient as primary implementation
        services.AddSingleton<ILocalStorageClient, LocalStorageClient>();
    }

    private static void ValidateAndInitializeStorage(StorageConfiguration config)
    {
        // Validate the configuration
        config.Validate();

        // Create necessary directories
        Directory.CreateDirectory(config.GetNormalizedBasePath());
        Directory.CreateDirectory(Path.GetDirectoryName(config.GetNormalizedMetadataPath())!);

        // Initialize the static helper
        StoragePathHelper.Initialize(config);
    }
} 