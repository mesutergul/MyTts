using MyTts.Models;

namespace MyTts.Services.Interfaces
{
    public interface INotificationService
    {
        Task SendNotificationAsync(string title, string message, NotificationType type, CancellationToken cancellationToken = default);
        Task SendErrorNotificationAsync(string title, string message, Exception? exception = null, CancellationToken cancellationToken = default);
    }

    public enum NotificationType
    {
        Info,
        Warning,
        Error,
        Success
    }
} 