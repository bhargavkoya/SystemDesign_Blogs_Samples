namespace ReplicaLab.Replication;

public sealed record ReadResult(string? Value, string ServedBy, long AppliedLsn, bool FellBackToLeader);

public sealed record FailoverResult(bool Promoted, string Message, long LostWrites, string? NewLeader);

public sealed record FollowerStatus(string Name, long AppliedLsn, int LagMs, bool IsUp);

public sealed record ClusterStatus(string Leader, bool LeaderUp, long LastCommittedLsn, List<FollowerStatus> Followers);
