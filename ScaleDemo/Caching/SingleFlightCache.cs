using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace ScaleDemo.Caching;

// Cache-aside with two protections against a stampede:
//   1. Single-flight: concurrent misses on the same key share ONE load.
//   2. TTL jitter: entries written together do not all expire together.
public sealed class SingleFlightCache(IMemoryCache cache)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _inFlight = new();
    private int _hits;
    private int _misses;

    public int Hits => Volatile.Read(ref _hits);
    public int Misses => Volatile.Read(ref _misses);

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> load,
        TimeSpan ttl,
        CancellationToken ct = default) where T : notnull
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);

        // The first caller creates the Lazy; everyone else awaits the same task.
        var entry = _inFlight.GetOrAdd(
            key,
            k => new Lazy<Task<object>>(() => LoadAndStoreAsync(k, load, ttl)));

        try
        {
            // The shared load must not be tied to any one caller's token, otherwise
            // the first client to disconnect would cancel the load for everyone.
            // Each caller stops waiting on its own token instead.
            return (T)await entry.Value.WaitAsync(ct);
        }
        finally
        {
            // Remove only our own entry, never a newer one for the same key.
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<object>>>(key, entry));
        }
    }

    public void Remove(string key) => cache.Remove(key);

    private async Task<object> LoadAndStoreAsync<T>(
        string key, Func<CancellationToken, Task<T>> load, TimeSpan ttl) where T : notnull
    {
        // If load throws, nothing is cached and the next request retries.
        T value = await load(CancellationToken.None);

        // +/- 10% jitter so a batch of entries does not expire in the same instant.
        double jitter = 0.9 + Random.Shared.NextDouble() * 0.2;
        cache.Set(key, value, TimeSpan.FromMilliseconds(ttl.TotalMilliseconds * jitter));

        return value;
    }
}
