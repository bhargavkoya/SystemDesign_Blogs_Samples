using ShardLab.Models;
using ShardLab.Options;

namespace ShardLab.Sharding;

// Stands in for one database server. Memory use is modeled as records * (record + index entry),
// because indexes live in the same RAM as the data they point at.
public sealed class InMemoryShard
{
    private readonly object _gate = new();
    private readonly Dictionary<int, List<CheckIn>> _byChunk = new();
    private readonly Dictionary<long, List<CheckIn>> _byVenue = new(); // Local secondary index.
    private readonly long _capacityBytes;
    private readonly int _bytesPerRecord;
    private long _records;
    private volatile bool _down;
    private int _extraLatencyMs;

    public InMemoryShard(int id, ShardingOptions options)
    {
        Id = id;
        _capacityBytes = options.CapacityBytesPerShard;
        _bytesPerRecord = options.BytesPerCheckIn + options.BytesPerIndexEntry;
    }

    public int Id { get; }
    public long CapacityBytes => _capacityBytes;
    public long Records { get { lock (_gate) return _records; } }
    public long UsedBytes { get { lock (_gate) return _records * _bytesPerRecord; } }
    public double Utilization => (double)UsedBytes / _capacityBytes;

    public bool IsDown
    {
        get => _down;
        set => _down = value;
    }

    public int ExtraLatencyMs
    {
        get => Volatile.Read(ref _extraLatencyMs);
        set => Volatile.Write(ref _extraLatencyMs, value);
    }

    public bool TryAppend(int chunk, CheckIn checkIn)
    {
        ThrowIfDown();
        lock (_gate)
        {
            if (!HasRoomFor(1)) return false;
            Add(chunk, checkIn);
            return true;
        }
    }

    public IReadOnlyList<CheckIn> ReadUser(int chunk, long userId)
    {
        ThrowIfDown();
        lock (_gate)
        {
            return _byChunk.TryGetValue(chunk, out var list)
                ? list.Where(c => c.UserId == userId).ToArray()
                : Array.Empty<CheckIn>();
        }
    }

    public async Task<IReadOnlyList<CheckIn>> FindByVenueAsync(long venueId, CancellationToken ct)
    {
        ThrowIfDown();
        if (ExtraLatencyMs > 0) await Task.Delay(ExtraLatencyMs, ct); // Simulates a slow shard.

        lock (_gate)
        {
            return _byVenue.TryGetValue(venueId, out var list) ? list.ToArray() : Array.Empty<CheckIn>();
        }
    }

    public IReadOnlyList<(int Chunk, long Bytes)> ChunkSizes()
    {
        lock (_gate)
        {
            return _byChunk.Select(kv => (kv.Key, (long)kv.Value.Count * _bytesPerRecord)).ToList();
        }
    }

    public IReadOnlyList<CheckIn> ExtractChunk(int chunk)
    {
        lock (_gate)
        {
            return _byChunk.TryGetValue(chunk, out var list) ? list.ToArray() : Array.Empty<CheckIn>();
        }
    }

    // Refuses the whole chunk if it does not fit. A half-imported chunk is worse than none.
    public bool TryImportChunk(int chunk, IReadOnlyList<CheckIn> items)
    {
        ThrowIfDown();
        lock (_gate)
        {
            if (!HasRoomFor(items.Count)) return false;
            foreach (var item in items) Add(chunk, item);
            return true;
        }
    }

    public void RemoveChunk(int chunk)
    {
        lock (_gate)
        {
            if (!_byChunk.Remove(chunk, out var items)) return;

            foreach (var item in items)
            {
                if (!_byVenue.TryGetValue(item.VenueId, out var venueList)) continue;
                venueList.Remove(item);
                if (venueList.Count == 0) _byVenue.Remove(item.VenueId);
            }

            _records -= items.Count;
        }
    }

    // Caller holds _gate.
    private bool HasRoomFor(long extraRecords) =>
        (_records + extraRecords) * _bytesPerRecord <= _capacityBytes;

    // Caller holds _gate.
    private void Add(int chunk, CheckIn checkIn)
    {
        ListFor(_byChunk, chunk).Add(checkIn);
        ListFor(_byVenue, checkIn.VenueId).Add(checkIn); // Every write also pays for the index.
        _records++;
    }

    private static List<CheckIn> ListFor<TKey>(Dictionary<TKey, List<CheckIn>> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<CheckIn>();
            map[key] = list;
        }

        return list;
    }

    private void ThrowIfDown()
    {
        if (_down) throw new ShardUnavailableException(Id);
    }
}
