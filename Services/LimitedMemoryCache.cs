using Microsoft.Extensions.Caching.Memory;
using MyTts.Services.Interfaces;

namespace MyTts.Services
{
    public class LimitedMemoryCache<TKey, TValue> : ICache<TKey, TValue>
    {
        private readonly MemoryCache _cache;
        private readonly MemoryCacheEntryOptions _entryOptions;
        private readonly LinkedList<TKey> _keyOrder = new();
        private readonly object _lock = new();

        private readonly int _maxItems;
        private readonly int _softLimit;

        public LimitedMemoryCache(int softLimit = 80, int maxItems = 100)
        {
            _softLimit = softLimit;
            _maxItems = maxItems;

            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = maxItems
            });

            _entryOptions = new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = TimeSpan.FromHours(48)
            };
        }

        public void Set(TKey key, TValue value)
        {
            ArgumentNullException.ThrowIfNull(key);
            
            lock (_lock)
            {
                if (!_cache.TryGetValue(key, out _))
                {
                    _keyOrder.AddLast(key);
                }

                _cache.Set(key, value, _entryOptions);

                if (_keyOrder.Count > _softLimit)
                {
                    TrimToSoftLimit();
                }
            }
        }

        public TValue Get(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            
            lock (_lock)
            {
                TValue? result = default;
                return _cache.TryGetValue(key, out result) ? result! : default!;
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            ArgumentNullException.ThrowIfNull(key);
            value = default!;
            
            lock (_lock)
            {
                TValue? result = default;
                var found = _cache.TryGetValue(key, out result);
                value = result!;
                return found;
            }
        }

        public void Remove(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            lock(_lock)
            {
                _cache.Remove(key);
                _keyOrder.Remove(key);
            }
        }

        private void TrimToSoftLimit()
        {
            while (_keyOrder.Count > _softLimit && _keyOrder.First != null)
            {
                var oldestKey = _keyOrder.First.Value;
                if (oldestKey != null)
                {
                    _cache.Remove(oldestKey);
                    _keyOrder.RemoveFirst();
                }
            }
        }
    }

}
