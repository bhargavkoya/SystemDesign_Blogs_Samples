namespace ShardLab.Sharding;

public sealed record ShardStatus(
    int Id,
    long Records,
    long UsedBytes,
    long CapacityBytes,
    double Utilization,
    int OwnedChunks,
    bool IsDown,
    bool OverAlertRatio);
