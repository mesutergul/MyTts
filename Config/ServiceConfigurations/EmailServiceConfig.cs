using MyTts.Services;
using MyTts.Services.Interfaces;
using Microsoft.Extensions.Options;
using System.Net;
using Polly;
using System.Net.Mail;
using MyTts.Models;

namespace MyTts.Config.ServiceConfigurations
{
    public static class EmailServiceConfig
    {
        public static IServiceCollection AddEmailServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Configure email options with validation
            services.Configure<EmailConfig>(configuration.GetSection("Email"));

            // Register email service with all its dependencies
            services.AddSingleton<IEmailService, EmailService>();

            // Register SmtpClient as singleton with proper configuration
            services.AddSingleton<SmtpClient>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<EmailConfig>>().Value;
                var logger = sp.GetRequiredService<ILogger<EmailService>>();

                // Validate configuration
                if (string.IsNullOrEmpty(config.SenderEmail) || string.IsNullOrEmpty(config.SenderPassword))
                {
                    throw new InvalidOperationException("Email sender credentials are not configured");
                }

                if (config.SmtpPort != 587 && config.SmtpPort != 465)
                {
                    throw new InvalidOperationException("Invalid SMTP port. Must be 587 (TLS) or 465 (SSL)");
                }

                if (string.IsNullOrEmpty(config.SmtpServer))
                {
                    throw new InvalidOperationException("SMTP server is not configured");
                }

                try
                {
                    var client = new SmtpClient(config.SmtpServer, config.SmtpPort)
                    {
                        EnableSsl = config.EnableSsl,
                        Credentials = new NetworkCredential(config.SenderEmail, config.SenderPassword),
                        Timeout = config.TimeoutSeconds * 1000,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        UseDefaultCredentials = false
                    };

                    // Configure connection pooling
                    client.ServicePoint.MaxIdleTime = 10000; // 10 seconds
                    client.ServicePoint.ConnectionLimit = 1;
                    client.ServicePoint.Expect100Continue = false;

                    // Test connection
                    try
                    {
                        using var testMessage = new MailMessage
                        {
                            From = new MailAddress(config.SenderEmail, config.SenderName),
                            Subject = "Test",
                            Body = "Test",
                            BodyEncoding = System.Text.Encoding.UTF8,
                            SubjectEncoding = System.Text.Encoding.UTF8
                        };
                        testMessage.To.Add(config.SenderEmail);
                        testMessage.Headers.Add("X-Mailer", "MyTts Notification System");
                        testMessage.Headers.Add("X-Auto-Response-Suppress", "OOF, AutoReply");

                        client.Send(testMessage);
                        logger.LogInformation("SMTP connection test successful");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "SMTP connection test failed, but continuing with configuration");
                    }

                    return client;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to configure SMTP client");
                    throw;
                }
            });

            // Register Polly policies
            services.AddSingleton<ResiliencePipeline<SmtpResponse>>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<EmailConfig>>().Value;
                var policyFactory = sp.GetRequiredService<SharedPolicyFactory>();

                var retryPolicy = policyFactory.GetEmailRetryPolicy<SmtpResponse>(
                    maxRetries: config.MaxRetries,
                    retryDelaySeconds: config.RetryDelaySeconds);

                var circuitBreakerPolicy = policyFactory.GetEmailCircuitBreakerPolicy<SmtpResponse>(
                    exceptionsAllowedBeforeBreaking: 5,
                    durationOfBreakInMinutes: 1);

                return new ResiliencePipelineBuilder<SmtpResponse>()
                    .AddPipeline(retryPolicy)
                    .AddPipeline(circuitBreakerPolicy).Build();
            });

            return services;
        }
    }
}