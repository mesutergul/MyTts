namespace MyTts.Models
{
    public class SmtpResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; } // Made nullable to fix CS8618  
    }
}