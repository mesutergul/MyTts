using MyTts.Services.Interfaces;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Net.Http;
using System.Text.Json;

namespace MyTts.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly NotificationOptions _options;
        private readonly HttpClient _httpClient;

        public NotificationService(
            ILogger<NotificationService> logger,
            IOptions<NotificationOptions> options,
            HttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task SendNotificationAsync(string title, string message, NotificationType type, CancellationToken cancellationToken = default)
        {
            try
            {
                // Log the notification
                _logger.LogInformation("Notification: {Title} - {Message} ({Type})", title, message, type);

                // Send to configured channels
                var tasks = new List<Task>();

                if (_options.EnableEmailNotifications)
                {
                    tasks.Add(SendEmailNotificationAsync(title, message, type, cancellationToken));
                }

                if (_options.EnableSlackNotifications)
                {
                    tasks.Add(SendSlackNotificationAsync(title, message, type, cancellationToken));
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send notification: {Title}", title);
            }
        }

        public async Task SendErrorNotificationAsync(string title, string message, Exception? exception = null, CancellationToken cancellationToken = default)
        {
            var fullMessage = exception == null ? message : $"{message}\nException: {exception}";
            await SendNotificationAsync(title, fullMessage, NotificationType.Error, cancellationToken);
        }

        private async Task SendEmailNotificationAsync(string title, string message, NotificationType type, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_options.EmailFrom) || string.IsNullOrEmpty(_options.EmailTo))
            {
                _logger.LogWarning("Email notifications are enabled but EmailFrom or EmailTo is not configured");
                return;
            }

            var retryCount = 0;
            var maxRetries = _options.EmailSettings?.MaxRetries ?? 3;
            var retryDelay = TimeSpan.FromSeconds(_options.EmailSettings?.RetryDelaySeconds ?? 5);

            while (true)
            {
                try
                {
                    var email = new MimeMessage();
                    email.From.Add(new MailboxAddress(
                        _options.EmailSettings?.DisplayName ?? "TTS Notification System",
                        _options.EmailFrom));
                    email.To.Add(new MailboxAddress("Admin", _options.EmailTo));
                    email.Subject = $"[{type}] {title}";

                    if (!string.IsNullOrEmpty(_options.EmailSettings?.ReplyTo))
                    {
                        email.ReplyTo.Add(new MailboxAddress("Support", _options.EmailSettings.ReplyTo));
                    }

                    var bodyBuilder = new BodyBuilder
                    {
                        TextBody = $"""
                            Notification Type: {type}
                            Title: {title}
                            Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
                            
                            Message:
                            {message}
                            """
                    };

                    email.Body = bodyBuilder.ToMessageBody();

                    using var smtp = new SmtpClient();
                    await smtp.ConnectAsync(
                        _options.SmtpServer,
                        _options.SmtpPort,
                        _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                        cancellationToken);

                    if (!string.IsNullOrEmpty(_options.SmtpUsername))
                    {
                        await smtp.AuthenticateAsync(_options.SmtpUsername, _options.SmtpPassword, cancellationToken);
                    }

                    await smtp.SendAsync(email, cancellationToken);
                    await smtp.DisconnectAsync(true, cancellationToken);

                    _logger.LogInformation("Email notification sent successfully to {Recipient}", _options.EmailTo);
                    return;
                }
                catch (Exception ex) when (retryCount < maxRetries)
                {
                    retryCount++;
                    _logger.LogWarning(ex, 
                        "Failed to send email notification (Attempt {RetryCount} of {MaxRetries}). Retrying in {Delay} seconds...",
                        retryCount, maxRetries, retryDelay.TotalSeconds);

                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(retryDelay, cancellationToken);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email notification after {RetryCount} attempts", retryCount);
                    throw;
                }
            }
        }

        private async Task SendSlackNotificationAsync(string title, string message, NotificationType type, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_options.SlackWebhookUrl))
            {
                _logger.LogWarning("Slack notifications are enabled but webhook URL is not configured");
                return;
            }

            try
            {
                var color = type switch
                {
                    NotificationType.Error => "#FF0000",
                    NotificationType.Warning => "#FFA500",
                    NotificationType.Success => "#00FF00",
                    _ => "#0000FF"
                };

                var payload = new
                {
                    attachments = new[]
                    {
                        new
                        {
                            color,
                            title,
                            text = message,
                            footer = $"Type: {type} | Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_options.SlackWebhookUrl, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Slack notification sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Slack notification");
                throw;
            }
        }
    }

    public class NotificationOptions
    {
        public bool EnableEmailNotifications { get; set; }
        public bool EnableSlackNotifications { get; set; }
        public string? EmailFrom { get; set; }
        public string? EmailTo { get; set; }
        public string? SlackWebhookUrl { get; set; }
        public string? SmtpServer { get; set; }
        public int SmtpPort { get; set; } = 587;
        public bool UseSsl { get; set; } = true;
        public string? SmtpUsername { get; set; }
        public string? SmtpPassword { get; set; }
        public EmailSettings? EmailSettings { get; set; }
    }

    public class EmailSettings
    {
        public string? DisplayName { get; set; }
        public string? ReplyTo { get; set; }
        public int MaxRetries { get; set; } = 3;
        public int RetryDelaySeconds { get; set; } = 5;
        public int TimeoutSeconds { get; set; } = 30;
    }
} 