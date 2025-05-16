using Microsoft.Extensions.Options;
using MyTts.Storage;

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

        // Register disk configurations 
        services.AddSingleton(sp =>
        {
            var storageConfig = sp.GetRequiredService<IOptions<StorageConfiguration>>().Value;
            var disks = new Dictionary<string, DiskConfiguration>();

            foreach (var disk in storageConfig.Disks)
            {
                disks[disk.Key] = new DiskConfiguration
                {
                    Driver = disk.Value.Driver,
                    Root = disk.Value.Root,
                    Config = disk.Value.Config ?? new DiskConfig()
                };
            }

            return disks;
        });

        return services;
    }
} 