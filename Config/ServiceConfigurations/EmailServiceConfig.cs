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