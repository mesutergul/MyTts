using MyTts.Data.Entities;
using Microsoft.EntityFrameworkCore;
using MyTts.Models;

namespace MyTts.Data.Context
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Mp3Meta> Mp3Metas { get; set; }
        public DbSet<News> News { get; set; }
        public DbSet<HaberKonumlari> HaberKonumlari { get; set; }
        //public DbSet<HaberSummaryDto> HaberSummary { get; set; }

        //protected override void OnModelCreating(ModelBuilder modelBuilder)
        //{
        //    modelBuilder.Entity<HaberSummaryDto>().HasNoKey();
        //}
    }
}