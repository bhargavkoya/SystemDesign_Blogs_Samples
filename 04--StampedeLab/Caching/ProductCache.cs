using System.Collections.Concurrent;
using StampedeLab.Data;

namespace StampedeLab.Caching;

public enum CacheMode
{
    /// <summary>Every caller that sees a miss queries the database. The stampede.</summary>
    Naive,

    /// <summary>One caller per key loads; everyone else awaits that same load.</summary>
    SingleFlight,

    /// <summary>Single-flight, plus: an expired entry is served while one background refresh runs.</summary>
    ServeStale,
}

public enum CacheOutcome { Hit, Miss, Stale }

public sealed record CacheResult(Product Value, CacheOutcome Outcome);

public sealed record CacheOptions(TimeSpan Ttl, double JitterFraction, TimeSpan StaleWindow);

public sealed class ProductCache(SlowDatabase db, CacheOptions options, ILogger<ProductCache> logger)
{
    // SoftExpiresAt is the TTL. HardExpiresAt is how long the entry may still be served stale.
    private sealed record Entry(Product Value, DateTimeOffset SoftExpiresAt, DateTimeOffset HardExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    // One in-flight load per key. Lazy guarantees the loader body runs once even if GetOrAdd races.
    private readonly ConcurrentDictionary<string, Lazy<Task<Product>>> _inflight = new();

    public async Task<CacheResult> GetAsync(int id, CacheMode mode)
    {
        var key = KeyFor(id);
        var now = DateTimeOffset.UtcNow;

        if (_entries.TryGetValue(key, out var entry))
        {
            if (now < entry.SoftExpiresAt)
                return new CacheResult(entry.Value, CacheOutcome.Hit);

            // Expired, but still inside the stale window: answer now, refresh behind the caller's back.
            if (mode == CacheMode.ServeStale && now < entry.HardExpiresAt)
            {
                StartBackgroundRefreshIfNeeded(key, id);
                return new CacheResult(entry.Value, CacheOutcome.Stale);
            }
        }

        var product = mode == CacheMode.Naive
            ? await LoadAndStoreAsync(key, id)
            : await LoadSingleFlightAsync(key, id);

        return new CacheResult(product, CacheOutcome.Miss);
    }

    // ---- loading ---------------------------------------------------------------------------------

    private async Task<Product> LoadSingleFlightAsync(string key, int id)
    {
        // GetOrAdd may run the factory for several racing callers, but only the winner's Lazy is ever
        // evaluated, so the database sees one query.
        var load = _inflight.GetOrAdd(key, _ => NewSharedLoad(key, id));
        try
        {
            return await load.Value;
        }
        finally
        {
            // Remove only our own entry. A failed load is therefore never remembered: the next caller retries.
            _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<Product>>>(key, load));
        }
    }

    private Lazy<Task<Product>> NewSharedLoad(string key, int id) => new(async () =>
    {
        // Re-check: a flight that just finished may have stored a fresh value between our miss and now.
        if (_entries.TryGetValue(key, out var e) && DateTimeOffset.UtcNow < e.SoftExpiresAt)
            return e.Value;

        // Deliberately no CancellationToken from the request here. This work is shared by many callers,
        // so one caller disconnecting must not cancel the load for everybody else.
        return await LoadAndStoreAsync(key, id);
    });

    private async Task<Product> LoadAndStoreAsync(string key, int id)
    {
        // If this throws, the existing entry is left exactly as it was. A failed query is not a reason
        // to delete or overwrite a cached value (see the Facebook postmortem in the post).
        var product = await db.GetProductAsync(id);

        var now = DateTimeOffset.UtcNow;
        var soft = now + JitteredTtl();
        _entries[key] = new Entry(product, soft, soft + options.StaleWindow);
        return product;
    }

    private void StartBackgroundRefreshIfNeeded(string key, int id)
    {
        var load = NewSharedLoad(key, id);
        if (_inflight.TryAdd(key, load))          // only the caller that wins TryAdd starts the refresh
            _ = ObserveRefreshAsync(key, load);
    }

    private async Task ObserveRefreshAsync(string key, Lazy<Task<Product>> load)
    {
        try
        {
            await load.Value;
        }
        catch (DatabaseException ex)
        {
            // The stale entry stays in the cache and keeps being served. The next request tries again.
            logger.LogWarning("Background refresh of {Key} failed, serving stale: {Message}", key, ex.Message);
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<Product>>>(key, load));
        }
    }

    // TTL +/- JitterFraction, so keys written together do not all expire in the same instant.
    private TimeSpan JitteredTtl()
    {
        var factor = 1 + (Random.Shared.NextDouble() * 2 - 1) * options.JitterFraction;
        return options.Ttl * factor;
    }

    private static string KeyFor(int id) => $"product:{id}";

    // ---- lab hooks (not part of a real cache API) ---------------------------------------------------

    public void Clear() => _entries.Clear();

    /// <summary>Makes the TTL elapse now. The entry stays inside its stale window.</summary>
    public void ExpireNow(int id)
    {
        var key = KeyFor(id);
        if (_entries.TryGetValue(key, out var e))
            _entries[key] = e with { SoftExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(-1) };
    }

    public async Task WaitForBackgroundWorkAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!_inflight.IsEmpty && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
    }
}
