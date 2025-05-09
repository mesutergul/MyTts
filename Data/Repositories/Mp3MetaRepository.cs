using MyTts.Data.Context;
using MyTts.Data.Interfaces;
using AutoMapper;

namespace MyTts.Data.Repositories
{
    public class Mp3MetaRepository : Repository<Entities.Mp3Meta, Interfaces.IMp3>, IMp3MetaRepository
    {
        public Mp3MetaRepository(IAppDbContextFactory contextFactory, IMapper? mapper, ILogger<Mp3MetaRepository> logger) : base(contextFactory, mapper, logger) { }

        // Mp3File'a özel metodları burada implemente edebilirsin

    }
}