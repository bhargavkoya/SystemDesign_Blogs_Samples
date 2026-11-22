namespace ShardLab.Sharding;

public enum ScatterPolicy
{
    // Any missing shard fails the whole query.
    Strict,

    // Return what the healthy shards have and say which shards are missing.
    Partial
}
