namespace StampedeLab.Data;

public sealed record DatabaseOptions(int MaxConcurrentQueries, TimeSpan QueryLatency, TimeSpan QueueTimeout);

public sealed record DatabaseStats(long QueriesStarted, long QueriesFailed, int PeakConcurrentQueries);

/// <summary>
/// A stand-in for a real database with a fixed capacity. Queries beyond MaxConcurrentQueries wait in a queue,
/// and give up after QueueTimeout. That is the property that makes a stampede dangerous: the database does
/// not slow down gracefully, it starts rejecting work.
/// </summary>
public sealed class SlowDatabase(DatabaseOptions options)
{
    private readonly SemaphoreSlim _slots = new(options.MaxConcurrentQueries);
    private int _inFlight;
    private int _peakInFlight;
    private long _queriesStarted;
    private long _queriesFailed;

    /// <summary>When true, every query that reaches the database fails. Used to simulate an outage.</summary>
    public volatile bool Failing;

    public async Task<Product> GetProductAsync(int id)
    {
        Interlocked.Increment(ref _queriesStarted);

        if (!await _slots.WaitAsync(options.QueueTimeout))
        {
            Interlocked.Increment(ref _queriesFailed);
            throw new DatabaseOverloadedException();
        }

        try
        {
            RecordPeak(Interlocked.Increment(ref _inFlight));
            await Task.Delay(options.QueryLatency);

            if (Failing)
            {
                Interlocked.Increment(ref _queriesFailed);
                throw new DatabaseUnavailableException();
            }

            return new Product(id, $"Product {id}", 19.99m, DateTimeOffset.UtcNow);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            _slots.Release();
        }
    }

    public DatabaseStats Snapshot() => new(
        Interlocked.Read(ref _queriesStarted),
        Interlocked.Read(ref _queriesFailed),
        Volatile.Read(ref _peakInFlight));

    public void ResetStats()
    {
        Interlocked.Exchange(ref _queriesStarted, 0);
        Interlocked.Exchange(ref _queriesFailed, 0);
        Volatile.Write(ref _peakInFlight, 0);
    }

    private void RecordPeak(int current)
    {
        int seen;
        while (current > (seen = Volatile.Read(ref _peakInFlight)))
        {
            if (Interlocked.CompareExchange(ref _peakInFlight, current, seen) == seen) return;
        }
    }
}
