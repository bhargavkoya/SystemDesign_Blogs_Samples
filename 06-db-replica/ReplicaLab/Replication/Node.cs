namespace ReplicaLab.Replication;

/// <summary>
/// One database node. Not thread-safe on purpose:
/// Cluster serialises every access behind a single lock.
/// </summary>
public sealed class Node(string name)
{
    private readonly Dictionary<string, string> _data = new();

    public string Name { get; } = name;

    // The highest log entry this node has applied.
    public long AppliedLsn { get; private set; }

    // How long a committed write waits before this node applies it.
    public int LagMs { get; set; }

    public bool IsUp { get; set; } = true;

    public void Apply(LogEntry entry)
    {
        _data[entry.Key] = entry.Value;
        AppliedLsn = entry.Lsn;
    }

    public string? Get(string key) => _data.GetValueOrDefault(key);
}
