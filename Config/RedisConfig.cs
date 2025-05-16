using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace MyTts.Config
{
    public class RedisConfig : IValidateOptions<RedisConfig>
    {
        [Required]
        public string ConnectionString { get; set; } = "localhost:6379";

        [Required]
        public string InstanceName { get; set; } = "MyTts_";

        public int DatabaseId { get; set; } = 0;

        [Range(1, 1440)]
        public int DefaultExpirationMinutes { get; set; } = 60;

        [Range(1, 10)]
        public int MaxRetryAttempts { get; set; } = 3;

        [Range(100, 10000)]
        public int RetryDelayMilliseconds { get; set; } = 1000;

        [Range(1, 1000)]
        public int MaxPoolSize { get; set; } = 100;

        [Range(0, 100)]
        public int MinPoolSize { get; set; } = 10;

        [Range(1000, 60000)]
        public int ConnectionTimeoutMs { get; set; } = 5000;

        [Range(1000, 60000)]
        public int OperationTimeoutMs { get; set; } = 5000;

        public bool EnableCompression { get; set; } = true;

        public ValidateOptionsResult Validate(string? name, RedisConfig options)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(options.ConnectionString))
                errors.Add("Redis connection string is required");

            if (string.IsNullOrEmpty(options.InstanceName))
                errors.Add("Redis instance name is required");

            if (options.DefaultExpirationMinutes < 1 || options.DefaultExpirationMinutes > 1440)
                errors.Add("DefaultExpirationMinutes must be between 1 and 1440");

            if (options.MaxRetryAttempts < 1 || options.MaxRetryAttempts > 10)
                errors.Add("MaxRetryAttempts must be between 1 and 10");

            if (options.RetryDelayMilliseconds < 100 || options.RetryDelayMilliseconds > 10000)
                errors.Add("RetryDelayMilliseconds must be between 100 and 10000");

            if (options.MaxPoolSize < 1 || options.MaxPoolSize > 1000)
                errors.Add("MaxPoolSize must be between 1 and 1000");

            if (options.MinPoolSize < 0 || options.MinPoolSize > 100)
                errors.Add("MinPoolSize must be between 0 and 100");

            if (options.ConnectionTimeoutMs < 1000 || options.ConnectionTimeoutMs > 60000)
                errors.Add("ConnectionTimeoutMs must be between 1000 and 60000");

            if (options.OperationTimeoutMs < 1000 || options.OperationTimeoutMs > 60000)
                errors.Add("OperationTimeoutMs must be between 1000 and 60000");

            return errors.Count > 0
                ? ValidateOptionsResult.Fail(errors)
                : ValidateOptionsResult.Success;
        }
    }
}