//using MyTts.Data.Entities;
//using MyTts.Data.Interfaces;

//namespace MyTts.Data.Repositories
//{
//    public class NullNewsRepository : IRepository<News, INews>
//    {
//        public Task AddAsync(News entity) => Task.CompletedTask;
//        public void Delete(News entity) { }
//        public Task<bool> ExistByIdAsync(int id) => Task.FromResult(false);
//        public Task<IEnumerable<News>> FindAsync(Func<News, bool> predicate) => Task.FromResult(Enumerable.Empty<News>());

//        public Task<IEnumerable<News>> GetAllAsync() => Task.FromResult(Enumerable.Empty<News>());
//        public Task<News> GetByIdAsync(int id) => Task.FromResult<News>(null);
//        public Task SaveChangesAsync() => Task.CompletedTask;
//        public void Update(News entity) { }
//    }
//}
