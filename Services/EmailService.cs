using System.Net.Mail;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services.Interfaces;
using Polly;

namespace MyTts.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly EmailConfig _emailConfig;
        private readonly IAsyncPolicy _retryPolicy;
        private readonly IAsyncPolicy _circuitBreakerPolicy;

        public EmailService(
            ILogger<EmailService> logger,
            IOptions<EmailConfig> emailConfig,
            IEnumerable<IAsyncPolicy> policies)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emailConfig = emailConfig?.Value ?? throw new ArgumentNullException(nameof(emailConfig));
            
            var policyList = policies.ToList();
            _retryPolicy = policyList[0];
            _circuitBreakerPolicy = policyList[1];
        }

        public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            await SendEmailAsync(new List<string> { to }, subject, body, isHtml);
        }

        public async Task SendEmailAsync(List<string> to, string subject, string body, bool isHtml = false)
        {
            var context = new Context();
            context["recipient"] = string.Join(", ", to);

            try
            {
                await _circuitBreakerPolicy
                    .WrapAsync(_retryPolicy)
                    .ExecuteAsync(async (ctx) =>
                    {
                        using var client = new SmtpClient(_emailConfig.SmtpServer, _emailConfig.SmtpPort)
                        {
                            EnableSsl = _emailConfig.EnableSsl,
                            Credentials = new System.Net.NetworkCredential(_emailConfig.SenderEmail, _emailConfig.SenderPassword),
                            Timeout = _emailConfig.TimeoutSeconds * 1000, // Convert seconds to milliseconds
                            DeliveryMethod = SmtpDeliveryMethod.Network,
                            UseDefaultCredentials = false
                        };

                        using var message = new MailMessage
                        {
                            From = new MailAddress(_emailConfig.SenderEmail, _emailConfig.SenderName),
                            Subject = subject,
                            Body = body,
                            IsBodyHtml = isHtml,
                            Priority = MailPriority.Normal
                        };

                        // Add headers to help with deliverability
                        message.Headers.Add("X-Mailer", "MyTts Notification System");
                        message.Headers.Add("X-Priority", "3");
                        message.Headers.Add("X-MSMail-Priority", "Normal");

                        if (!string.IsNullOrEmpty(_emailConfig.ReplyTo))
                        {
                            message.ReplyToList.Add(_emailConfig.ReplyTo);
                        }

                        foreach (var recipient in to)
                        {
                            message.To.Add(recipient);
                        }

                        try
                        {
                            await client.SendMailAsync(message);
                            _logger.LogInformation("Email sent successfully to {Recipients}", string.Join(", ", to));
                        }
                        catch (SmtpException ex)
                        {
                            _logger.LogError(ex, "SMTP error while sending email to {Recipients}. Status code: {StatusCode}, Response: {Response}",
                                string.Join(", ", to), ex.StatusCode, ex.Message);
                            throw;
                        }
                    }, context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Recipients} after all retries. Error: {Error}",
                    string.Join(", ", to), ex.Message);
                throw;
            }
        }
    }
}