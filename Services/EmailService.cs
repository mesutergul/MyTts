using System.Net.Mail;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services.Interfaces;
using Polly;
using MyTts.Config.ServiceConfigurations;
using MyTts.Middleware;

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
                _logger.LogInformation("Attempting to send email to {Recipients} using SMTP server {Server}:{Port}",
                    string.Join(", ", to), _emailConfig.SmtpServer, _emailConfig.SmtpPort);

                var policy = _policyFactory.GetEmailPolicy<object>();
                await policy.ExecuteAsync<object>(async (ctx) =>
                {
                    using var message = new MailMessage
                    {
                        From = new MailAddress(_emailConfig.SenderEmail, _emailConfig.SenderName),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = isHtml,
                        Priority = MailPriority.Normal,
                        BodyEncoding = System.Text.Encoding.UTF8,
                        SubjectEncoding = System.Text.Encoding.UTF8
                    };

                    // Add headers to help with deliverability
                    message.Headers.Add("X-Mailer", "MyTts Notification System");
                    message.Headers.Add("X-Priority", "3");
                    message.Headers.Add("X-MSMail-Priority", "Normal");
                    message.Headers.Add("X-Auto-Response-Suppress", "OOF, AutoReply");

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
                        _logger.LogDebug("Sending email with subject: {Subject}", subject);
                        await _smtpClient.SendMailAsync(message);
                        _logger.LogInformation("Email sent successfully to {Recipients}", string.Join(", ", to));
                        return null!;
                    }
                    catch (SmtpException ex)
                    {
                        var errorDetails = new
                        {
                            StatusCode = ex.StatusCode,
                            Server = _emailConfig.SmtpServer,
                            Port = _emailConfig.SmtpPort,
                            Recipients = string.Join(", ", to),
                            Subject = subject,
                            IsHtml = isHtml,
                            ErrorMessage = ex.Message,
                            StackTrace = ex.StackTrace
                        };

                        // Check for protocol violation specifically
                        if (ex.Message.Contains("protocol violation", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogError(ex, 
                                "SMTP protocol violation detected. Details: {@ErrorDetails}", errorDetails);
                            throw new EmailServiceUnavailableException(
                                $"SMTP protocol violation with server {_emailConfig.SmtpServer}: {ex.Message}", ex);
                        }

                        switch (ex.StatusCode)
                        {
                            case SmtpStatusCode.ServiceNotAvailable:
                                _logger.LogError(ex, 
                                    "SMTP service unavailable. Details: {@ErrorDetails}", errorDetails);
                                throw new EmailServiceUnavailableException("SMTP service is currently unavailable", ex);

                            case SmtpStatusCode.ServiceClosingTransmissionChannel:
                                _logger.LogError(ex, 
                                    "SMTP service closed transmission channel. Details: {@ErrorDetails}", errorDetails);
                                throw new EmailServiceUnavailableException("SMTP service closed the connection", ex);

                            case SmtpStatusCode.ExceededStorageAllocation:
                                _logger.LogError(ex, 
                                    "Email storage allocation exceeded. Details: {@ErrorDetails}", errorDetails);
                                throw new EmailStorageException("Email storage allocation exceeded", ex);

                            case SmtpStatusCode.MailboxBusy:
                            case SmtpStatusCode.MailboxUnavailable:
                                _logger.LogError(ex, 
                                    "Recipient mailbox unavailable. Details: {@ErrorDetails}", errorDetails);
                                throw new InvalidEmailRecipientException($"Recipient mailbox unavailable: {string.Join(", ", to)}", ex);

                            case SmtpStatusCode.TransactionFailed:
                                _logger.LogError(ex, 
                                    "SMTP transaction failed. Details: {@ErrorDetails}", errorDetails);
                                throw new EmailTransactionException("SMTP transaction failed", ex);

                            default:
                                _logger.LogError(ex, 
                                    "SMTP general failure. Details: {@ErrorDetails}", errorDetails);
                                throw new EmailException("SMTP general failure", ex);
                        }
                    }
                });
            }
            catch (Exception ex) when (ex is not EmailException)
            {
                var errorDetails = new
                {
                    Server = _emailConfig.SmtpServer,
                    Port = _emailConfig.SmtpPort,
                    Recipients = string.Join(", ", to),
                    Subject = subject,
                    IsHtml = isHtml,
                    ErrorMessage = ex.Message,
                    StackTrace = ex.StackTrace
                };

                _logger.LogError(ex, 
                    "Failed to send email to {Recipients} after all retries. Error: {Error}. Details: {@ErrorDetails}",
                    string.Join(", ", to), 
                    ex.Message,
                    errorDetails);
                throw new EmailException($"Failed to send email: {ex.Message}", ex);
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }
    }
}