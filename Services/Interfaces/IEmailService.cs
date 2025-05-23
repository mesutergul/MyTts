namespace MyTts.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body, bool isHtml = false);
        Task SendEmailAsync(List<string> to, string subject, string body, bool isHtml = false);
    }
}