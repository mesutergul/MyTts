﻿using Microsoft.EntityFrameworkCore;

namespace MyTts.Data.Context
{
    public class DunyaDbContext : DbContext
    {
        public DunyaDbContext(DbContextOptions<DunyaDbContext> options) : base(options)
        {
        }
      //  public DbSet<News> News { get; set; }
      //  public DbSet<HaberKonumlari> HaberKonumlari { get; set; }
    }
}
