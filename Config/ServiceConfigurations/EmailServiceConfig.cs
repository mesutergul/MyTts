using MyTts.Services;
using MyTts.Services.Interfaces;
using MyTts.Config;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;

namespace MyTts.Config.ServiceConfigurations
{
    public static class EmailServiceConfig
    {
        public static IServiceCollection AddEmailServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure email options with validation
            services.Configure<EmailConfig>(configuration.GetSection("Email"));

            // Register email service with all its dependencies
            services.AddScoped<IEmailService, EmailService>();
            services.AddScoped<EmailService>();

            // Register SmtpClient as singleton
            services.AddSingleton<SmtpClient>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<EmailConfig>>().Value;
                var client = new SmtpClient(config.SmtpServer, config.SmtpPort)
                {
                    EnableSsl = config.EnableSsl,
                    Credentials = new NetworkCredential(config.SenderEmail, config.SenderPassword),
                    Timeout = config.TimeoutSeconds * 1000,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false
                };

                // Set up client validation
                if (string.IsNullOrEmpty(config.SenderEmail) || string.IsNullOrEmpty(config.SenderPassword))
                {
                    throw new InvalidOperationException("Email sender credentials are not configured");
                }

                if (config.SmtpPort != 587 && config.SmtpPort != 465)
                {
                    throw new InvalidOperationException("Invalid SMTP port. Must be 587 (TLS) or 465 (SSL)");
                }

                return client;
            });

            // Register Polly policies
            services.AddSingleton<IAsyncPolicy>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<EmailConfig>>().Value;
                var logger = sp.GetRequiredService<ILogger<EmailService>>();

                return Policy
                    .Handle<SmtpException>()
                    .Or<InvalidOperationException>()
                    .WaitAndRetryAsync(config.MaxRetries, retryAttempt =>
                        TimeSpan.FromSeconds(config.RetryDelaySeconds * Math.Pow(2, retryAttempt)),
                        onRetry: (exception, timeSpan, retryCount, context) =>
                        {
                            logger.LogWarning(exception,
                                "Retry {RetryCount} after {Delay}ms for email to {Recipient}. Error: {Error}",
                                retryCount, timeSpan.TotalMilliseconds, context["recipient"], exception.Message);
                        });
            });

            services.AddSingleton<IAsyncPolicy>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EmailService>>();

                return Policy
                    .Handle<SmtpException>()
                    .Or<InvalidOperationException>()
                    .CircuitBreakerAsync(
                        exceptionsAllowedBeforeBreaking: 2,
                        durationOfBreak: TimeSpan.FromMinutes(5),
                        onBreak: (exception, duration) =>
                        {
                            logger.LogWarning(exception,
                                "Circuit breaker opened for {Duration} seconds due to SMTP issues. Error: {Error}",
                                duration.TotalSeconds, exception.Message);
                        },
                        onReset: () =>
                        {
                            logger.LogInformation("Circuit breaker reset - email service is healthy again");
                        },
                        onHalfOpen: () =>
                        {
                            logger.LogInformation("Circuit breaker half-open - testing email service health");
                        });
            });

            return services;
        }
    }
}