﻿using Microsoft.Extensions.Caching.Memory;
using MyTts.Services.Interfaces;

namespace MyTts.Services
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Extensions.Caching.Memory;

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

        public void SetRange(Dictionary<TKey, TValue> existingHashList)
        {
            if (existingHashList == null)
                throw new ArgumentNullException(nameof(existingHashList));

            lock (_lock)
            {
                foreach (var kvp in existingHashList)
                {
                    if (!_cache.TryGetValue(kvp.Key, out _))
                    {
                        _keyOrder.AddLast(kvp.Key);
                    }
                    _cache.Set(kvp.Key, kvp.Value, _entryOptions);
                }

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
                return _cache.TryGetValue(key, out TValue result) ? result : default!;
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            ArgumentNullException.ThrowIfNull(key);
            value = default!;

            lock (_lock)
            {
                var found = _cache.TryGetValue(key, out TValue result);
                value = result!;
                return found;
            }
        }

        public void Remove(TKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            lock (_lock)
            {
                _cache.Remove(key);
                _keyOrder.Remove(key);
            }
        }

        /// <summary>
        /// Number of items currently in the cache.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _keyOrder.Count;
                }
            }
        }

        /// <summary>
        /// True if the cache has no entries.
        /// </summary>
        public bool IsEmpty()
        {
            lock (_lock)
            {
                return _keyOrder.Count == 0;
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
