using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Entities
{
    [Table("Haberler")]
    public class News : BaseEntity, INews
    {
        [Column("ozet")]
        public string? Summary { get; set; }
      
        [Column("baslik")]
        public string? Title { get; set; }
    }
}