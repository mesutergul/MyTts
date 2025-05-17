using MyTts.Data.Entities;
using MyTts.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace MyTts.Data.Interfaces
{
    public interface IMp3MetaRepository : IRepository<Mp3Meta, Mp3Dto>
    {
        Task<bool> ExistByIdAsync(int id, CancellationToken cancellationToken);
        Task<List<int>> GetExistingFileIdsInLast500Async(List<int> values, CancellationToken cancellationToken);
        Task<Mp3Dto> GetByColumnAsync(Expression<Func<Mp3Meta, bool>> predicate, CancellationToken cancellationToken);
    }
}
