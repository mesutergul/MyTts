using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using MyTts.Models;

namespace MyTts.Data.Repositories
{
    public class NewsRepository : Repository<AppDbContext , News, NewsDto>, INewsRepository
    {
        public NewsRepository(IGenericDbContextFactory<AppDbContext> contextFactory, IMapper mapper, ILogger<NewsRepository> logger) : base(contextFactory, mapper, logger) { }
        // News'a özel metodları burada implemente edebilirsin
        public async Task<List<HaberSummaryDto>> getSummary(int top, MansetType mansetType, CancellationToken token)
        {
            _logger.LogInformation("Connected to: " + _context.Database.GetDbConnection().Database);
            
            var query = await _context.HaberKonumlari
                .AsNoTracking()
                .Where(k => k.KonumAdi == "ana manşet")
                .OrderBy(k => k.Sirano)
                .Take(top)
                .Select(k => new HaberSummaryDto
                {
                    IlgiId = k.IlgiId,
                    Baslik = k.News == null ? string.Empty : k.News.Title ?? string.Empty,
                    Ozet = k.News == null ? string.Empty : k.News.Summary ?? string.Empty
                })
                .ToListAsync(token);

            return query;
        }
    }
}
