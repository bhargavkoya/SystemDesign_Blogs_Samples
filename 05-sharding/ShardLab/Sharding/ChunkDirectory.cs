namespace ShardLab.Sharding;

// The directory is the whole trick. user -> chunk is a fixed hash and never changes.
// chunk -> shard is a lookup table, so moving data is a table update, not a rehash of every key.
public sealed class ChunkDirectory
{
    private readonly int[] _owner;

    public ChunkDirectory(int chunkCount, int shardCount)
    {
        _owner = new int[chunkCount];
        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            _owner[chunk] = chunk % shardCount; // Even by chunk COUNT. Not by bytes. That gap is the point.
        }
    }

    // Stable 64-bit mix (Murmur3 finalizer). Do not use string.GetHashCode() here:
    // it is randomized per process, so the same key would land on different chunks after a restart.
    public int ChunkOf(long userId)
    {
        var x = (ulong)userId;
        x ^= x >> 33;
        x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33;
        x *= 0xc4ceb9fe1a85ec53UL;
        x ^= x >> 33;
        return (int)(x % (ulong)_owner.Length);
    }

    public int OwnerOf(int chunk) => Volatile.Read(ref _owner[chunk]);

    public void Assign(int chunk, int shardId) => Volatile.Write(ref _owner[chunk], shardId);

    public int OwnedBy(int shardId) => _owner.Count(owner => owner == shardId);
}
