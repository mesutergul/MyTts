//using System;
//using System.ComponentModel.DataAnnotations;
//using System.ComponentModel.DataAnnotations.Schema;
//using MyTts.Data.Entities;
//using MyTts.Data.Interfaces;

//namespace MyTts.Models
//{
//    [Table("Haberler_Speeches")]
//    public class Mp3Meta : BaseEntity, IMp3
//    {
//        [Key]
//        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
//        public int Id { get; set; }
//        [Column("haber_id")]
//        public required int FileId { get; set; }
//        [Column("ses_dosyasi_url")]
//        public required string FileUrl { get; set; }

//        [Column("dil")]
//        public required string Language { get; set; }

//        [Column("Created_Date")]
//        public DateTime? CreatedDate { get; set; }

//        [Column("durum")]
//        public bool Enabled { get; set; }

//    }
//}