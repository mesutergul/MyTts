using Microsoft.Extensions.Options;
using MyTts.Storage;
using MyTts.Services;
using MyTts.Services.Interfaces;
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

        // Get the configuration instance for initialization
        var storageConfig = configuration.GetSection("Storage").Get<StorageConfiguration>()
            ?? throw new InvalidOperationException("Storage configuration is missing");

        // Validate and initialize storage
        ValidateAndInitializeStorage(storageConfig);

        // Register the configuration as singleton for direct injection
        services.AddSingleton(storageConfig);

        // Register local storage service
        services.AddSingleton<ILocalStorageService, LocalStorageService>();

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