namespace ShardLab.Sharding;

public sealed class ShardUnavailableException(int shardId)
    : Exception($"Shard {shardId} is unavailable.")
{
    public int ShardId { get; } = shardId;
}
