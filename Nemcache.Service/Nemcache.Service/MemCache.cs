﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using Nemcache.Service.Eviction;
using Nemcache.Service.Notifications;
using Nemcache.Service.Reactive;

namespace Nemcache.Service
{
    // TODO: remove expired items
    internal class MemCache : IMemCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>();
        private readonly ICacheObserver _cacheObserver;
        private readonly IObservable<ICacheNotification> _combinedNotifications;
        private readonly IEvictionStrategy _evictionStrategy;
        private readonly IScheduler _scheduler;
        private readonly Subject<ICacheNotification> _notificationsSubject;
        private int _currentSequenceId;
        private IDisposable _interval;

        public MemCache(ulong capacity) : this(capacity, Scheduler.Default)
        {
            
        }

        public MemCache(ulong capacity, IScheduler scheduler)
        {
            _scheduler = scheduler;
            Capacity = capacity;
            _notificationsSubject = new Subject<ICacheNotification>();
            _combinedNotifications = Observable.Defer(CreateNotifications);

            var lruStrategy = new LRUEvictionStrategy(this);
            _evictionStrategy = lruStrategy;
            _cacheObserver = lruStrategy;

            _interval = Observable.Interval(TimeSpan.FromSeconds(1), _scheduler).Subscribe(_ => RemoveExpired());

        }

        private void RemoveExpired()
        {
            var expired = _cache.ToArray().Where(ce => ce.Value.IsExpired(_scheduler));
            foreach (var kv in expired)
            {
                Remove(kv.Key);
            }
        }

        public ulong Capacity { get; set; }

        public ulong Used
        {
            // TODO: this accounted for a few percent of time on profiles. 
            get { return (ulong) _cache.Values.Select(e => (long) e.Data.Length).Sum(); }
        }

        public void Clear()
        {
            var eventId = Interlocked.Increment(ref _currentSequenceId);
            _cache.Clear();
            _notificationsSubject.OnNext(new ClearNotification {EventId = eventId});
        }

        public bool Touch(string key, DateTime exptime)
        {
            bool success = false;
            CacheEntry entry;
            if (_cache.TryGetValue(key, out entry))
            {
                var eventId = Interlocked.Increment(ref _currentSequenceId);
                CacheEntry touched = entry;
                touched.EventId = eventId;
                touched.Expiry = exptime;
                _cacheObserver.Use(key);
                _cache.TryUpdate(key, touched, entry); // OK to fail if something else has updated?
                success = true;
                _notificationsSubject.OnNext(new TouchNotification
                    {
                        Key = key,
                        Expiry = exptime,
                        EventId = eventId
                    });
            }
            return success;
        }

        // I dont like this method as it assumes a format of the data.
        public bool Mutate(string key, ulong incr, out byte[] resultDataOut, bool positive)
        {
            byte[] resultData = null;
            bool result = _cache.TryUpdate(
                key, entry =>
                    {
                        var value = ulong.Parse(Encoding.ASCII.GetString(entry.Data));
                        if (positive)
                            value += incr;
                        else
                            value -= incr;
                        resultData = Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture));
                        entry.Data = resultData;
                        return entry;
                    });

            resultDataOut = result ? resultData : null;
            return result;
        }

        public bool Cas(string key, ulong flags, DateTime exptime, ulong casUnique, byte[] newData)
        {
            CacheEntry entry;
            if (_cache.TryGetValue(key, out entry))
            {
                if (entry.CasUnique == casUnique)
                {
                    var spaceRequired = Math.Abs(newData.Length - entry.Data.Length);
                    if (spaceRequired > 0)
                        MakeSpaceForNewEntry(spaceRequired);
                    _cacheObserver.Use(key);
                    var eventId = Interlocked.Increment(ref _currentSequenceId);
                    var newValue = new CacheEntry
                        {
                            CasUnique = casUnique,
                            Data = newData,
                            Expiry = exptime,
                            Flags = flags,
                            EventId = eventId,
                        };
                    
                    var updated = _cache.TryUpdate(key, newValue, entry);
                    // notify cas update
                    return updated;
                }
                return false;
            }
            CasStore(key, flags, exptime, casUnique, newData);
            return true;
        }

        public bool Replace(string key, ulong flags, DateTime exptime, byte[] data)
        {
            _cacheObserver.Use(key);
            MakeSpaceForNewEntry(data.Length); // In the case of replace this could be offset by the existing value
            var eventId = Interlocked.Increment(ref _currentSequenceId);
            var replaced = _cache.TryUpdate(key, e => new CacheEntry
                {
                    Data = data,
                    Expiry = exptime,
                    Flags = flags,
                    EventId = eventId
                });
            if (replaced)
            {
                _notificationsSubject.OnNext(new StoreNotification
                    {
                        Key = key,
                        Data = data,
                        Expiry = exptime,
                        Flags = flags,
                        Operation = StoreOperation.Replace,
                        EventId = eventId
                    });
            }
            return replaced;
        }

        public bool Add(string key, ulong flags, DateTime exptime, byte[] data)
        {
            MakeSpaceForNewEntry(data.Length); // In the case of replace this could be offset by the existing value
            var eventId = Interlocked.Increment(ref _currentSequenceId);
            var entry = new CacheEntry
                {
                    Data = data,
                    Expiry = exptime,
                    Flags = flags,
                    EventId = eventId
                };
            bool result = _cache.TryAdd(key, entry);

            if (result)
            {
                _cacheObserver.Use(key);
                _notificationsSubject.OnNext(new StoreNotification
                    {
                        Key = key,
                        Data = data,
                        Expiry = exptime,
                        Flags = flags,
                        Operation = StoreOperation.Add,
                        EventId = eventId
                    });
            }
            return result;
        }

        public bool Append(string key, ulong flags, DateTime exptime, byte[] data, bool prepend)
        {
            _cacheObserver.Use(key);
            MakeSpaceForNewEntry(data.Length); // In the case of replace this could be offset by the existing value
            var eventId = Interlocked.Increment(ref _currentSequenceId);
            var exists = _cache.TryUpdate(key, e =>
                {
                    var newEntry = e;
                    newEntry.Data = prepend
                                        ? e.Data.Concat(data).ToArray()
                                        : data.Concat(e.Data).ToArray();
                    newEntry.EventId = eventId;
                    return newEntry;
                });
            if (exists)
            {
                _notificationsSubject.OnNext(
                    new StoreNotification
                        {
                            Key = key,
                            Data = data,
                            Expiry = exptime,
                            Flags = flags,
                            Operation = prepend ? StoreOperation.Append : StoreOperation.Prepend,
                            EventId = eventId
                        });
            }
            return exists;
        }

        public bool Store(string key, ulong flags, byte[] data, DateTime exptime)
        {
            _cacheObserver.Use(key);
            MakeSpaceForNewEntry(data.Length); // In the case of replace this could be offset by the existing value
            var eventId = Interlocked.Increment(ref _currentSequenceId);
            _cache[key] = new CacheEntry
                {
                    Data = data,
                    Expiry = exptime,
                    Flags = flags,
                    EventId = eventId
                };
            _notificationsSubject.OnNext(
                new StoreNotification
                    {
                        Key = key,
                        Data = data,
                        Expiry = exptime,
                        Flags = flags,
                        Operation = StoreOperation.Store,
                        EventId = eventId
                    });
            return true;
        }

        public bool Remove(string key)
        {
            _cacheObserver.Remove(key);
            CacheEntry entry;
            var eventId = Interlocked.Increment(ref _currentSequenceId);
            bool removed = _cache.TryRemove(key, out entry);
            if (removed)
                _notificationsSubject.OnNext(new RemoveNotification {Key = key, EventId = eventId});
            return removed;
        }

        public IEnumerable<KeyValuePair<string, CacheEntry>> Retrieve(IEnumerable<string> keys)
        {
            var tmp = keys.ToArray();
            foreach (var key in tmp)
            {
                _cacheObserver.Use(key);
            }
            return from key in tmp
                   where _cache.ContainsKey(key)
                   select KeyValuePair.Create(key, _cache[key]);
        }

        public IObservable<ICacheNotification> Notifications
        {
            get { return _combinedNotifications; }
        }

        public IEnumerable<string> Keys
        {
            get { return _cache.Keys; }
        }


        private IObservable<ICacheNotification> CreateNotifications()
        {
            var currentCache = _cache.ToArray();
            var addOperations = currentCache.Select(e =>
                                                    new StoreNotification
                                                        {
                                                            Key = e.Key,
                                                            Data = e.Value.Data,
                                                            Expiry = e.Value.Expiry,
                                                            Flags = e.Value.Flags,
                                                            Operation = StoreOperation.Add,
                                                            EventId = e.Value.EventId,
                                                            IsSnapshot = true
                                                        });

            return addOperations.ToObservable().Combine(_notificationsSubject);
        }

        public void MakeSpaceForNewEntry(int length)
        {
            while (!HasAvailableSpace((ulong) length))
            {
                _evictionStrategy.EvictEntry();
            }
        }

        private bool HasAvailableSpace(ulong length)
        {
            return Capacity >= Used + length;
        }

        private void CasStore(string key, ulong flags, DateTime exptime, ulong casUnique, byte[] newData)
        {
            _cacheObserver.Use(key);
            var eventId2 = Interlocked.Increment(ref _currentSequenceId);
            _cache[key] = new CacheEntry
                {
                    CasUnique = casUnique,
                    Data = newData,
                    Expiry = exptime,
                    Flags = flags,
                    EventId = eventId2,
                };
            // TODO: notfiy Cas
        }
    }
}