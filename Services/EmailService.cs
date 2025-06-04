using System.Net.Mail;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services.Interfaces;
using Polly;
using MyTts.Config.ServiceConfigurations;

namespace MyTts.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly EmailConfig _emailConfig;
        private readonly SharedPolicyFactory _policyFactory;
        private readonly SmtpClient _smtpClient;

        public EmailService(
            ILogger<EmailService> logger,
            IOptions<EmailConfig> emailConfig,
            SharedPolicyFactory policyFactory,
            SmtpClient smtpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _emailConfig = emailConfig?.Value ?? throw new ArgumentNullException(nameof(emailConfig));
            _policyFactory = policyFactory ?? throw new ArgumentNullException(nameof(policyFactory));
            _smtpClient = smtpClient ?? throw new ArgumentNullException(nameof(smtpClient));
        }

        public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
        {
            await SendEmailAsync(new List<string> { to }, subject, body, isHtml);
        }

        public async Task SendEmailAsync(List<string> to, string subject, string body, bool isHtml = false)
        {
            var context = ResilienceContextPool.Shared.Get();
            context.Properties.Set(new ResiliencePropertyKey<string>("recipient"), string.Join(", ", to));

            try
            {
                var policy = _policyFactory.GetEmailPolicy<object>();
                await policy.ExecuteAsync<object>(async (ctx) =>
                {
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
                        await _smtpClient.SendMailAsync(message);
                        _logger.LogInformation("Email sent successfully to {Recipients}", string.Join(", ", to));
                        return null!;
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