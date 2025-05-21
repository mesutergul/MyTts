using MyTts.Services.Interfaces;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;

namespace MyTts.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ILogger<NotificationService> _logger;
        private readonly NotificationOptions _options;
        private readonly HttpClient _httpClient;
        private readonly IEmailService _emailService;

        public NotificationService(
            ILogger<NotificationService> logger,
            IOptions<NotificationOptions> options,
            HttpClient httpClient,
            IEmailService emailService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
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

                if (_options.EnableSlackNotifications && !string.IsNullOrEmpty(_options.SlackWebhookUrl))
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
            if (string.IsNullOrEmpty(_options.EmailTo))
            {
                _logger.LogWarning("Email notifications are enabled but EmailTo is not configured");
                return;
            }

            try
            {
                var subject = $"[{type}] {title}";
                var body = $"""
                    Notification Type: {type}
                    Title: {title}
                    Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
                    
                    Message:
                    {message}
                    """;

                await _emailService.SendEmailAsync(_options.EmailTo, subject, body);
                _logger.LogInformation("Email notification sent successfully to {Recipient}", _options.EmailTo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email notification to {Recipient}", _options.EmailTo);
                throw;
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
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Slack webhook returned non-success status code: {StatusCode}. Response: {Response}", 
                        response.StatusCode, errorContent);
                    return;
                }

                _logger.LogInformation("Slack notification sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Slack notification");
                // Don't throw the exception, just log it
            }
        }
    }

    public class NotificationOptions
    {
        public bool EnableEmailNotifications { get; set; }
        public bool EnableSlackNotifications { get; set; }
        public string? EmailTo { get; set; }
        public string? SlackWebhookUrl { get; set; }
    }
} 