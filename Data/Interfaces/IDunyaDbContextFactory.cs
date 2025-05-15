using MyTts.Data.Context;

namespace MyTts.Data.Interfaces
{
    public interface IDunyaDbContextFactory<TContext> where TContext : DunyaDbContext
    {
        DunyaDbContext? Create();
    }
}
