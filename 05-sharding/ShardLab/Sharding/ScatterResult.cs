using ShardLab.Models;

namespace ShardLab.Sharding;

public sealed record ScatterResult(IReadOnlyList<CheckIn> Items, IReadOnlyList<int> MissingShards)
{
    public bool Complete => MissingShards.Count == 0;
}
