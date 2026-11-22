namespace ShardLab.Sharding;

public sealed record ChunkMove(int Chunk, int FromShard, int ToShard, long Bytes);
