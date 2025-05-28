
namespace MyTts.Services.Interfaces
{
    public interface ICache<TKey, TValue>
    {
        void Set(TKey key, TValue value);
        TValue Get(TKey key);
        bool TryGetValue(TKey key, out TValue value);
        void Remove(TKey key);
        bool IsEmpty();
        void SetRange(Dictionary<TKey, TValue> existingHashList);
        int Count { get; }
    }

}
