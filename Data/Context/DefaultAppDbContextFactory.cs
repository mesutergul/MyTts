using MyTts.Data.Context;
using MyTts.Data.Interfaces;

namespace MyTts.Data.Context
{
    public class DefaultAppDbContextFactory : IAppDbContextFactory
    {
        private readonly AppDbContext? _context;
        public DefaultAppDbContextFactory(AppDbContext? context)
        {
            _context = context;
        }
        public AppDbContext? Create() => _context;
    }
}
