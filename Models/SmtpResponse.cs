namespace MyTts.Models
{
    public class SmtpResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }
    }
}