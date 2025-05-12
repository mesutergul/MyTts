using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Entities
{
    public abstract class BaseEntity : IEntity
{
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

    }
}