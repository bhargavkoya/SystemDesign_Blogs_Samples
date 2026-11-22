namespace ShardLab.Sharding;

public sealed class ScatterIncompleteException(IReadOnlyList<int> missingShards)
    : Exception($"Strict query failed: shards {string.Join(", ", missingShards)} did not answer.")
{
    public IReadOnlyList<int> MissingShards { get; } = missingShards;
}
