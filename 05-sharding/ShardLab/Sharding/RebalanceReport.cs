namespace ShardLab.Sharding;

public sealed record RebalanceReport(IReadOnlyList<ChunkMove> Moves, IReadOnlyList<ShardStatus> After);
