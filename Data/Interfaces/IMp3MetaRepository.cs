
using MyTts.Data.Entities;
using MyTts.Models;

namespace MyTts.Data.Interfaces
{
    public interface IMp3MetaRepository : IRepository<Mp3Meta, Mp3Dto>
    {
        // Mp3File'a özel metotlar burada tanımlanabilir
    }
}
