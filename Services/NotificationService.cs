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
        private bool _slackEnabled;
        private readonly string? _slackWebhookUrl;

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

            // Validate Slack configuration at startup
            if (_options.EnableSlackNotifications)
            {
                if (string.IsNullOrEmpty(_options.SlackWebhookUrl))
                {
                    _logger.LogInformation("Slack notifications are enabled but webhook URL is not configured. Slack notifications will be disabled.");
                    _slackEnabled = false;
                    _slackWebhookUrl = null;
                }
                else if (!IsValidSlackWebhookUrl(_options.SlackWebhookUrl))
                {
                    _logger.LogInformation("Slack notifications are enabled but webhook URL is invalid. Slack notifications will be disabled.");
                    _slackEnabled = false;
                    _slackWebhookUrl = null;
                }
                else
                {
                    // Test the webhook URL at startup
                    try
                    {
                        var testPayload = new { text = "Testing Slack webhook configuration..." };
                        var content = new StringContent(
                            JsonSerializer.Serialize(testPayload),
                            System.Text.Encoding.UTF8,
                            "application/json");

                        var response = _httpClient.PostAsync(_options.SlackWebhookUrl, content).GetAwaiter().GetResult();
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            _logger.LogInformation("Slack webhook URL validation failed. Status: {StatusCode}, Error: {Error}. Slack notifications will be disabled.",
                                response.StatusCode, errorContent);
                            _slackEnabled = false;
                            _slackWebhookUrl = null;
                        }
                        else
                        {
                            _slackEnabled = true;
                            _slackWebhookUrl = _options.SlackWebhookUrl;
                            _logger.LogInformation("Slack notifications are enabled and properly configured");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation(ex, "Failed to validate Slack webhook URL. Slack notifications will be disabled.");
                        _slackEnabled = false;
                        _slackWebhookUrl = null;
                    }
                }
            }
            else
            {
                _slackEnabled = false;
                _slackWebhookUrl = null;
                _logger.LogInformation("Slack notifications are disabled");
            }
        }

        public Task SendNotificationAsync(string title, string message, NotificationType type, CancellationToken cancellationToken = default)
        {
            // Log the notification
            _logger.LogInformation("Notification: {Title} - {Message} ({Type})", title, message, type);

            // Fire and forget email notification
            if (_options.EnableEmailNotifications)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendEmailNotificationAsync(title, message, type, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send email notification");
                    }
                }, cancellationToken);
            }

            // Fire and forget Slack notification only if properly configured
            if (_slackEnabled && !string.IsNullOrEmpty(_slackWebhookUrl))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendSlackNotificationAsync(title, message, type, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation("Failed to send Slack notification: {Error}", ex.Message);
                        // Disable Slack notifications for future calls
                        _slackEnabled = false;
                    }
                }, cancellationToken);
            }

            return Task.CompletedTask;
        }

        public Task SendErrorNotificationAsync(string title, string message, Exception? exception = null, CancellationToken cancellationToken = default)
        {
            var fullMessage = exception == null ? message : $"{message}\nException: {exception}";
            return SendNotificationAsync(title, fullMessage, NotificationType.Error, cancellationToken);
        }

        private async Task SendEmailNotificationAsync(string title, string message, NotificationType type, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_options.EmailTo))
            {
                _logger.LogInformation("Email notifications are enabled but EmailTo is not configured");
                return;
            }

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

        private async Task SendSlackNotificationAsync(string title, string message, NotificationType type, CancellationToken cancellationToken)
        {
            if (!_slackEnabled || string.IsNullOrEmpty(_slackWebhookUrl))
            {
                _logger.LogInformation("Slack notifications are not properly configured");
                return;
            }

            var payload = new
            {
                text = $"*[{type}] {title}*\n{message}",
                username = "TTS Notification Bot",
                icon_emoji = type switch
                {
                    NotificationType.Success => ":white_check_mark:",
                    NotificationType.Warning => ":warning:",
                    NotificationType.Error => ":x:",
                    _ => ":information_source:"
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json");

            try
            {
                var response = await _httpClient.PostAsync(_slackWebhookUrl, content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogInformation("Slack API returned {StatusCode}: {Error}", response.StatusCode, errorContent);
                    _slackEnabled = false;
                    return;
                }
                _logger.LogInformation("Slack notification sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Failed to send Slack notification: {Error}", ex.Message);
                _slackEnabled = false;
            }
        }

        private static bool IsValidSlackWebhookUrl(string? webhookUrl)
        {
            if (string.IsNullOrEmpty(webhookUrl))
                return false;

            try
            {
                var uri = new Uri(webhookUrl);
                return uri.Scheme == "https" &&
                       uri.Host == "hooks.slack.com" &&
                       uri.AbsolutePath.StartsWith("/services/", StringComparison.OrdinalIgnoreCase) &&
                       uri.AbsolutePath.Split('/').Length >= 4; // Valid Slack webhook URLs have at least 4 segments
            }
            catch (UriFormatException)
            {
                return false;
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