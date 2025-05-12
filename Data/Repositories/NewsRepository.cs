
using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using MyTts.Models;

namespace MyTts.Data.Repositories
{
    public class NewsRepository : Repository<News, INews>, INewsRepository
    {
        public NewsRepository(IAppDbContextFactory contextFactory, IMapper? mapper, ILogger<NewsRepository> logger) : base(contextFactory, mapper, logger) { }
        // News'a özel metodları burada implemente edebilirsin
        public async Task<List<HaberSummaryDto>> getSummary(int top, MansetType mansetType, CancellationToken token)
        {
            if (_dbSet == null)
            {
                _logger.LogWarning("DbSet is not available. Skipping GetById check.");
                return new List<HaberSummaryDto>();
            }
            var query = await _context.HaberKonumlari
                .Include(k => k.News) // Ensure navigation property is loaded
                .Where(k => k.KonumAdi == "ana manşet")
                .OrderBy(k => k.Sirano)
                .Take(20)
                .ToListAsync();

            return _mapper.Map<List<HaberSummaryDto>>(query);
            //var query = await (from k in _context.HaberKonumlari
            //                   join h in _context.News on k.IlgiId equals h.Id
            //                   where k.KonumAdi == "ana manşet"
            //                   orderby k.Sirano
            //                   select new { k, h })
            //          .Take(20)
            //          .ToListAsync();

            //return _mapper.Map<List<HaberSummaryDto>>(query.Select(x => (x.k, x.h)).ToList());
        }
    }
}
