
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
                .Include(k => k.News) // Ensure navigation property is loaded
                .Where(k => k.KonumAdi == "ana manşet")
                .OrderBy(k => k.Sirano)
                .Take(20)
                .ToListAsync();

            // The mapping profile applied here is HaberMappingProfile
            return _mapper.Map<List<HaberSummaryDto>>(query);
        }
    }
}
