using System.ComponentModel.DataAnnotations.Schema;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Entities
{
    [Table("Haber_Konumlari")]
    public class HaberKonumlari : BaseEntity
    {
        [Column("ilgi_id")]
        [ForeignKey(nameof(News))]
        public int IlgiId { get; set; }
        [Column("Konum_Adi")]
        public string KonumAdi { get; set; } = string.Empty;
        [Column("Sirano")]
        public int Sirano { get; set; }

        public News? News { get; set; } // Navigation property
    }
}
