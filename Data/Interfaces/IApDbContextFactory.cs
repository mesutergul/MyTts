using MyTts.Data.Context;

namespace MyTts.Data.Interfaces
{
    public interface IAppDbContextFactory
    {
        AppDbContext? Create();
    }
}