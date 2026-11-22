using Microsoft.Extensions.Options;
using ShardLab.Models;
using ShardLab.Options;

namespace ShardLab.Sharding;

public sealed class ShardRouter
{
    private readonly ShardingOptions _options;
    private readonly ILogger<ShardRouter> _log;
    private readonly InMemoryShard[] _shards;
    private readonly ChunkDirectory _directory;
    private readonly ReaderWriterLockSlim[] _chunkLocks;
    private readonly int[] _alerted;
    private readonly object _rebalanceGate = new();
    private long _nextId;

    public ShardRouter(IOptions<ShardingOptions> options, ILogger<ShardRouter> log)
    {
        _options = options.Value;
        _log = log;
        _shards = Enumerable.Range(0, _options.ShardCount).Select(id => new InMemoryShard(id, _options)).ToArray();
        _directory = new ChunkDirectory(_options.ChunkCount, _options.ShardCount);
        _chunkLocks = Enumerable.Range(0, _options.ChunkCount).Select(_ => new ReaderWriterLockSlim()).ToArray();
        _alerted = new int[_options.ShardCount];
    }

    public bool HasShard(int id) => id >= 0 && id < _shards.Length;
    public void SetDown(int id, bool down) => _shards[id].IsDown = down;
    public void SetLatency(int id, int ms) => _shards[id].ExtraLatencyMs = ms;

    // DECISION 1: when a shard is full we refuse the write. A real database would keep accepting
    // and spill to disk, which is exactly how the Foursquare shard went from slow to down.
    // Refusing keeps the failure small and loud: one key range errors, the rest keep working.
    public WriteOutcome Write(long userId, long venueId)
    {
        var chunk = _directory.ChunkOf(userId);
        var gate = _chunkLocks[chunk];

        // Writers share the read lock. Only a chunk migration takes the write lock.
        gate.EnterReadLock();
        try
        {
            var shard = _shards[_directory.OwnerOf(chunk)];
            var checkIn = new CheckIn(Interlocked.Increment(ref _nextId), userId, venueId, DateTimeOffset.UtcNow);

            if (!shard.TryAppend(chunk, checkIn)) return WriteOutcome.ShardFull;

            RaiseAlertOnce(shard);
            return WriteOutcome.Accepted;
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    public IReadOnlyList<CheckIn> ReadUser(long userId)
    {
        var chunk = _directory.ChunkOf(userId);
        var gate = _chunkLocks[chunk];

        gate.EnterReadLock();
        try
        {
            return _shards[_directory.OwnerOf(chunk)].ReadUser(chunk, userId);
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    // DECISION 2: a venue lookup is not keyed by user, so every shard must be asked.
    // Strict fails the whole query if any shard is missing. Partial answers with what it has
    // and names the missing shards. Which is right depends on whether a wrong answer is worse than no answer.
    public async Task<ScatterResult> QueryVenueAsync(long venueId, ScatterPolicy policy, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_options.ScatterTimeoutMs);

        var replies = await Task.WhenAll(_shards.Select(shard => ProbeAsync(shard, venueId, deadline.Token)));

        var missing = replies.Where(r => !r.Ok).Select(r => r.ShardId).ToArray();
        if (missing.Length > 0 && policy == ScatterPolicy.Strict)
        {
            throw new ScatterIncompleteException(missing);
        }

        // DECISION 3: Distinct() is not decoration. During a chunk move the chunk briefly exists on
        // two shards (import before remove), so a scan in that window sees it twice.
        var items = replies
            .Where(r => r.Ok)
            .SelectMany(r => r.Items)
            .Distinct()
            .OrderByDescending(c => c.Id)
            .ToList();

        return new ScatterResult(items, missing);
    }

    public IReadOnlyList<ShardStatus> Snapshot() =>
        _shards.Select(s => new ShardStatus(
            s.Id,
            s.Records,
            s.UsedBytes,
            s.CapacityBytes,
            Math.Round(s.Utilization, 3),
            _directory.OwnedBy(s.Id),
            s.IsDown,
            s.Utilization >= _options.AlertRatio)).ToList();

    // DECISION 4: move one chunk at a time and pause writes to THAT chunk only (1/200 of the key space).
    // The alternative is dual-writing during the copy: no pause, but two code paths and a reconciliation
    // problem if the process dies halfway. A short pause on a small slice was the cheaper trade here.
    public RebalanceReport Rebalance(double spread, int maxMoves)
    {
        lock (_rebalanceGate) // One rebalance at a time. Two would fight over the same chunks.
        {
            var moves = new List<ChunkMove>();

            for (var i = 0; i < maxMoves; i++)
            {
                var live = _shards.Where(s => !s.IsDown).ToArray();
                if (live.Length < 2) break;

                var hot = live.MaxBy(s => s.UsedBytes)!;
                var cold = live.MinBy(s => s.UsedBytes)!;
                var gapBytes = hot.UsedBytes - cold.UsedBytes;
                if ((double)gapBytes / hot.CapacityBytes <= spread) break;

                // Biggest chunk that does not overshoot half the gap. Moving more would just flip
                // which shard is hot.
                var pick = hot.ChunkSizes()
                    .Where(c => c.Bytes <= gapBytes / 2)
                    .OrderByDescending(c => c.Bytes)
                    .ToList();
                if (pick.Count == 0) break;

                if (!MoveChunk(pick[0].Chunk, hot, cold)) break;
                moves.Add(new ChunkMove(pick[0].Chunk, hot.Id, cold.Id, pick[0].Bytes));
            }

            foreach (var shard in _shards.Where(s => s.Utilization < _options.AlertRatio))
            {
                Interlocked.Exchange(ref _alerted[shard.Id], 0); // Re-arm the alert.
            }

            return new RebalanceReport(moves, Snapshot());
        }
    }

    private bool MoveChunk(int chunk, InMemoryShard from, InMemoryShard to)
    {
        var gate = _chunkLocks[chunk];
        gate.EnterWriteLock();
        try
        {
            var items = from.ExtractChunk(chunk);

            // Order matters: copy, then flip, then delete. A crash between any two steps leaves the
            // chunk readable somewhere. Delete-then-copy would leave a window with the data nowhere.
            if (!to.TryImportChunk(chunk, items)) return false; // Target cannot hold it. Nothing changed.
            _directory.Assign(chunk, to.Id);
            from.RemoveChunk(chunk);
            return true;
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    private void RaiseAlertOnce(InMemoryShard shard)
    {
        if (shard.Utilization < _options.AlertRatio) return;
        if (Interlocked.Exchange(ref _alerted[shard.Id], 1) == 1) return;

        _log.LogWarning("Shard {ShardId} is at {Utilization:P0} of its budget. Rebalance or add capacity now.",
            shard.Id, shard.Utilization);
    }

    private static async Task<(int ShardId, bool Ok, IReadOnlyList<CheckIn> Items)> ProbeAsync(
        InMemoryShard shard, long venueId, CancellationToken ct)
    {
        try
        {
            return (shard.Id, true, await shard.FindByVenueAsync(venueId, ct));
        }
        catch (ShardUnavailableException)
        {
            return (shard.Id, false, Array.Empty<CheckIn>());
        }
        catch (OperationCanceledException)
        {
            return (shard.Id, false, Array.Empty<CheckIn>()); // Timed out waiting for this shard.
        }
    }
}
