using System.Collections.Concurrent;
using CapTimeoutDemo.Models;

namespace CapTimeoutDemo.Services;

/// <summary>
/// Two in-memory copies of the data. Node A (primary) accepts every write.
/// Node B (replica) only receives a write while the link is healthy; during a partition
/// the write is queued, and Heal() replays the queue in order (the "recover" step).
/// </summary>
public sealed class Cluster(Link link)
{
    private readonly ConcurrentDictionary<string, VersionedValue> _primary = new();
    private readonly ConcurrentDictionary<string, VersionedValue> _replica = new();
    private readonly List<(string Key, VersionedValue Value)> _pending = [];
    private readonly object _gate = new();

    public VersionedValue Write(string key, string value)
    {
        lock (_gate)
        {
            var version = _primary.TryGetValue(key, out var current) ? current.Version + 1 : 1;
            var next = new VersionedValue(value, version);

            _primary[key] = next; // Node A always accepts the write

            if (link.Partitioned)
            {
                _pending.Add((key, next)); // replication cannot cross the partition
            }
            else
            {
                _replica[key] = next;
            }

            return next;
        }
    }

    /// <summary>End the partition and replay everything Node B missed, oldest first.</summary>
    public int Heal()
    {
        lock (_gate)
        {
            link.SetPartitioned(false);

            foreach (var (key, value) in _pending)
            {
                _replica[key] = value;
            }

            var replayed = _pending.Count;
            _pending.Clear();
            return replayed;
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    public VersionedValue? ReadPrimary(string key) =>
        _primary.TryGetValue(key, out var value) ? value : null;

    public VersionedValue? ReadReplica(string key) =>
        _replica.TryGetValue(key, out var value) ? value : null;
}
