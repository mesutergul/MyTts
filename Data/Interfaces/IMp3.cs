
namespace MyTts.Data.Interfaces
{
    public interface IMp3 : IModel
    {
        int FileId { get; set; }
        string FileUrl { get; set; }
        string Language { get; set; }
        public bool Enabled { get; set; }
    }
}