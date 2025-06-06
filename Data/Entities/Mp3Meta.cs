using System.ComponentModel.DataAnnotations.Schema;

namespace MyTts.Data.Entities
{
    [Table("Haber_Ses_Dosyalari")]
    public class Mp3Meta : BaseEntity
    {       
        [Column("haber_id")]
        public required int FileId { get; set; }
        
        [Column("ses_dosyasi_url")]
        public required string FileUrl { get; set; }
        
        [Column("dil")]
        public required string Language { get; set; }

        [Column("tarih")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Column("durum")]
        public bool Enabled { get; set; }
       
        [Column("ozet_hash")]
        public string? OzetHash { get; set; }

        // Add other properties as needed, based on your database schema
    }
}