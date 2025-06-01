using MyTts.Data.Context;
using MyTts.Data.Entities;
using MyTts.Data.Interfaces;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using MyTts.Models;

namespace MyTts.Data.Repositories
{
    public class NewsRepository : Repository<AppDbContext, News, NewsDto>, INewsRepository
    {
        public NewsRepository(IGenericDbContextFactory<AppDbContext> contextFactory, IMapper mapper, ILogger<NewsRepository> logger) : base(contextFactory, mapper, logger) { }
        // News'a özel metodları burada implemente edebilirsin
        public async Task<List<HaberSummaryDto>> getSummary(int top, MansetType mansetType, CancellationToken token)
        {
            _logger.LogInformation("Connected to: " + _context.Database.GetDbConnection().Database);

            var query = await _context.HaberKonumlari
                .Where(k => k.KonumAdi == "ana manşet")
                .OrderBy(k => k.Sirano)
                .Join(
                    _context.News,
                    k => k.IlgiId,
                    h => h.Id,
                    (k, h) => new { Konum = k, Haber = h }
                )
                .Take(20)
                .Where(k => k.Haber != null && !ContainsBlockedContent(k.Haber.Summary ?? string.Empty, k.Haber.Id))
                .Select(k => new HaberSummaryDto
                {
                    IlgiId = k.Haber.Id,
                    Baslik = k.Haber == null ? string.Empty : k.Haber.Title ?? string.Empty,
                    Ozet = k.Haber == null ? string.Empty : k.Haber.Summary ?? string.Empty
                })
                .ToListAsync(token);

            return query;
        }
         private bool ContainsBlockedContent(string text, int id)
        {
            // Define blocked content categories
            var blockedCategories = new Dictionary<string, string[]>
            {
                ["violence"] = new[] {
                    // English
                    "kill", "murder", "attack", "weapon", "gun", "bomb", "terrorist",
                    "suicide", "abuse", "torture", "blood", "gore",
                    // Turkish
                    "öldür", "katliam", "saldırı", "silah", "bomba", "terörist",
                    "intihar", "istismar", "işkence", "kan", "şiddet"
                },
                ["hate"] = new[] {
                    // English
                    "racist", "nazi", "supremacist", "bigot", "hate speech",
                    "discriminate", "prejudice", "intolerant",
                    // Turkish
                    "ırkçı", "nazi", "üstün", "bağnaz", "nefret söylemi",
                    "ayrımcı", "önyargı", "hoşgörüsüz"
                },
                ["explicit"] = new[] {
                    // English
                    "porn", "sex", "nude", "explicit", "adult content",
                    "obscene", "lewd", "vulgar",
                    // Turkish
                    "porno", "seks", "çıplak", "müstehcen", "yetişkin içerik",
                    "edepsiz", "ahlaksız", "kaba"
                },
                ["illegal"] = new[] {
                    // English
                    "drug", "cocaine", "heroin", "meth", "illegal",
                    "hack", "crack", "pirate", "steal",
                    // Turkish
                    "uyuşturucu", "eroin", "metamfetamin", "yasadışı",
                    "hack", "korsan", "çal", "hırsızlık"
                },
                ["harmful"] = new[] {
                    // English
                    "suicide", "self-harm", "abuse", "exploit",
                    "scam", "fraud", "phishing",
                    // Turkish
                    "intihar", "kendine zarar", "istismar", "sömürü",
                    "dolandırıcılık", "sahte", "dolandırma"
                }
            };

            // Check for blocked content in parallel
            var blockedCategory = blockedCategories.AsParallel()
                .FirstOrDefault(category => category.Value.Any(term =>
                    text.Contains(term, StringComparison.OrdinalIgnoreCase)));

            if (blockedCategory.Key != null)
            {
                _logger.LogWarning("Content blocked for ID {Id} due to {Category} policy violation", id, blockedCategory.Key);
                // Fire and forget notification
                // _ = Task.Run(async () =>
                // {
                //     try
                //     {
                //         await _notificationService.SendNotificationAsync(
                //             "Content Policy Violation",
                //             $"Content blocked for ID {id} due to {blockedCategory.Key} policy violation",
                //             NotificationType.Warning);
                //     }
                //     catch (Exception ex)
                //     {
                //         _logger.LogError(ex, "Failed to send notification for content policy violation for ID {Id}", id);
                //     }
                // });
                return true;
            }
            return false;
        }
    }
}
