using System.ComponentModel.DataAnnotations.Schema;

namespace MyTts.Data.Entities
{
    [Table("Haberler")]
    public class News : BaseEntity
    {
        [Column("ozet")]
        public string? Summary { get; set; }
      
        [Column("baslik")]
        public string? Title { get; set; }
    }
}