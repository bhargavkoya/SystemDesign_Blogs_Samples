namespace ShardLab.Options;

public sealed class ShardingOptions
{
    public const string Section = "Sharding";

    // Physical shards. Three is enough to show imbalance without drowning the output.
    public int ShardCount { get; set; } = 3;

    // Logical chunks. Fixed for the life of the system: data moves chunk by chunk,
    // so this number caps how finely we can rebalance. 200 mirrors the Foursquare setup.
    public int ChunkCount { get; set; } = 200;

    // The "RAM budget" per shard. Crossing it is the Foursquare failure mode.
    public long CapacityBytesPerShard { get; set; } = 5_000_000;

    public int BytesPerCheckIn { get; set; } = 300;
    public int BytesPerIndexEntry { get; set; } = 40;

    // Alert early. The point is to act before the budget is gone, not at 100%.
    public double AlertRatio { get; set; } = 0.80;

    // How long a scatter-gather query waits for the slowest shard.
    public int ScatterTimeoutMs { get; set; } = 250;
}
