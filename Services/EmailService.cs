using System.Net.Mail;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services.Interfaces;
using Polly;
using Polly.Retry;

namespace MyTts.Services
{

    public class EmailService : IEmailService
    {
        private readonly EmailConfig _emailConfig;
        private readonly ILogger<EmailService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private const int EmailTimeoutSeconds = 30;

        public EmailService(IOptions<EmailConfig> emailConfig, ILogger<EmailService> logger)
        {
            _emailConfig = emailConfig.Value;
            _logger = logger;

            // Configure retry policy
            _retryPolicy = Policy
                .Handle<SmtpException>()
                .Or<IOException>()
                .WaitAndRetryAsync(
                    _emailConfig.MaxRetries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(exception,
                            "Retry {RetryCount} after {Delay}ms due to {ErrorType}",
                            retryCount,
                            timeSpan.TotalMilliseconds,
                            exception.GetType().Name);
                    });
        }

        public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            await SendEmailAsync(new List<string> { to }, subject, body, isHtml);
        }

        public async Task SendEmailAsync(List<string> to, string subject, string body, bool isHtml = false)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    using var message = new MailMessage
                    {
                        From = new MailAddress(_emailConfig.SenderEmail, _emailConfig.SenderName),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = isHtml
                    };

                    if (!string.IsNullOrEmpty(_emailConfig.ReplyTo))
                    {
                        message.ReplyToList.Add(_emailConfig.ReplyTo);
                    }

                    foreach (var recipient in to)
                    {
                        message.To.Add(recipient);
                    }

                    using var client = new SmtpClient(_emailConfig.SmtpServer, _emailConfig.SmtpPort)
                    {
                        EnableSsl = _emailConfig.EnableSsl,
                        Credentials = new System.Net.NetworkCredential(_emailConfig.SenderEmail, _emailConfig.SenderPassword),
                        Timeout = EmailTimeoutSeconds * 1000, // Convert seconds to milliseconds
                        DeliveryMethod = SmtpDeliveryMethod.Network
                    };

                    await client.SendMailAsync(message);
                    _logger.LogInformation("Email sent successfully to {Recipients}", string.Join(", ", to));
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Recipients} after {MaxRetries} retries",
                    string.Join(", ", to),
                    _emailConfig.MaxRetries);
                throw;
            }
        }
    }
}