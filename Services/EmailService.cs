using System.Net.Mail;
using Microsoft.Extensions.Options;
using MyTts.Config;
using MyTts.Services.Interfaces;

namespace MyTts.Services;

public class EmailService : IEmailService
{
    private readonly EmailConfig _emailConfig;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailConfig> emailConfig, ILogger<EmailService> logger)
    {
        _emailConfig = emailConfig.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
    {
        await SendEmailAsync(new List<string> { to }, subject, body, isHtml);
    }

    public async Task SendEmailAsync(List<string> to, string subject, string body, bool isHtml = false)
    {
        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_emailConfig.SenderEmail, _emailConfig.SenderName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };

            foreach (var recipient in to)
            {
                message.To.Add(recipient);
            }

            using var client = new SmtpClient(_emailConfig.SmtpServer, _emailConfig.SmtpPort)
            {
                EnableSsl = _emailConfig.EnableSsl,
                Credentials = new System.Net.NetworkCredential(_emailConfig.SenderEmail, _emailConfig.SenderPassword)
            };

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent successfully to {Recipients}", string.Join(", ", to));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipients}", string.Join(", ", to));
            throw;
        }
    }
} 