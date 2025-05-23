
namespace MyTts.Models
{
    public class Mp3Dto
    {
        public int FileId { get; set; }
        public string FileUrl { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        public bool Enabled { get; set; }
        public string? OzetHash { get; set; }
    }
}
