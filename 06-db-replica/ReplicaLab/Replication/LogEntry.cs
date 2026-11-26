namespace ReplicaLab.Replication;

/// <summary>
/// One committed write in the leader's log.
/// Lsn is the log sequence number. It only ever grows and is never reused.
/// </summary>
public sealed record LogEntry(long Lsn, string Key, string Value, long CommittedAtMs);
