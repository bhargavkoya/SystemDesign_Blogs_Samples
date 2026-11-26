using System.Diagnostics;

namespace ReplicaLab.Replication;

/// <summary>
/// One leader, N followers, asynchronous log shipping.
/// A single lock guards all state. That is a deliberate choice: this is a
/// teaching model, and one lock makes the ordering of writes, replication
/// ticks and failover easy to reason about. A real engine would not do this.
/// </summary>
public sealed class Cluster
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _log = new();
    private readonly List<Node> _followers;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    private Node _leader;
    private long _nextLsn = 1;
    private int _roundRobin;

    public Cluster()
    {
        _leader = new Node("db-leader");
        _followers =
        [
            new Node("db-follower-1") { LagMs = 20 },
            new Node("db-follower-2") { LagMs = 20 },
        ];
    }

    private long NowMs() => (long)Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;

    /// <summary>Commit on the leader and return as soon as the leader has it.</summary>
    public long Write(string key, string value)
    {
        lock (_gate)
        {
            if (!_leader.IsUp)
            {
                throw new InvalidOperationException("The leader is down. Writes are refused.");
            }

            var entry = new LogEntry(_nextLsn++, key, value, NowMs());
            _log.Add(entry);
            _leader.Apply(entry);

            // The caller gets this LSN back. It is the token for read-your-writes.
            return entry.Lsn;
        }
    }

    /// <summary>
    /// Called by the background worker. Ships committed entries to each follower
    /// once they are older than that follower's lag.
    /// </summary>
    public void ReplicateTick()
    {
        lock (_gate)
        {
            // A dead leader ships nothing. Anything it had not shipped yet is stuck on it.
            if (!_leader.IsUp)
            {
                return;
            }

            var now = NowMs();

            foreach (var follower in _followers)
            {
                if (!follower.IsUp)
                {
                    continue;
                }

                foreach (var entry in _log)
                {
                    if (entry.Lsn <= follower.AppliedLsn)
                    {
                        continue;
                    }

                    // The log is ordered by commit time, so the first entry that is
                    // too young means every later entry is too young as well.
                    if (now - entry.CommittedAtMs < follower.LagMs)
                    {
                        break;
                    }

                    follower.Apply(entry);
                }
            }
        }
    }

    /// <summary>
    /// Route a read. The decision lives here:
    /// wait up to waitBudget for a follower that has reached minLsn,
    /// then give up and read from the leader.
    /// </summary>
    public async Task<ReadResult> ReadAsync(string key, long minLsn, TimeSpan waitBudget, CancellationToken ct)
    {
        var waited = Stopwatch.StartNew();

        while (true)
        {
            lock (_gate)
            {
                var fresh = _followers
                    .Where(n => n.IsUp && n.AppliedLsn >= minLsn)
                    .ToList();

                if (fresh.Count > 0)
                {
                    var replica = fresh[_roundRobin % fresh.Count];
                    _roundRobin = (_roundRobin + 1) % 1_000_000;
                    return new ReadResult(replica.Get(key), replica.Name, replica.AppliedLsn, FellBackToLeader: false);
                }

                if (waited.Elapsed >= waitBudget)
                {
                    if (!_leader.IsUp)
                    {
                        throw new InvalidOperationException("No follower is fresh enough and the leader is down.");
                    }

                    return new ReadResult(_leader.Get(key), _leader.Name, _leader.AppliedLsn, FellBackToLeader: true);
                }
            }

            // Wait outside the lock so writes and replication keep moving.
            await Task.Delay(5, ct);
        }
    }

    /// <summary>
    /// Promote the most caught-up follower.
    /// If it is behind the last write the old leader acknowledged, those writes
    /// exist only on the old leader. Promoting anyway discards them, so that
    /// needs force = true.
    /// </summary>
    public FailoverResult Failover(bool force)
    {
        lock (_gate)
        {
            var best = _followers
                .Where(n => n.IsUp)
                .OrderByDescending(n => n.AppliedLsn)
                .FirstOrDefault();

            if (best is null)
            {
                return new FailoverResult(false, "No healthy follower to promote.", 0, null);
            }

            var lastAcknowledged = _nextLsn - 1;
            var lost = lastAcknowledged - best.AppliedLsn;

            if (lost > 0 && !force)
            {
                return new FailoverResult(
                    false,
                    $"Refused: {best.Name} is {lost} write(s) behind. Promoting it discards them.",
                    lost,
                    null);
            }

            // Drop what the new leader never saw. LSNs are NOT rewound,
            // so a token handed out for a lost write can never be satisfied by mistake.
            _log.RemoveAll(e => e.Lsn > best.AppliedLsn);

            _followers.Remove(best);
            _leader.IsUp = false; // fence the old leader
            _leader = best;

            return new FailoverResult(true, "Promoted.", lost, best.Name);
        }
    }

    public void KillLeader()
    {
        lock (_gate)
        {
            _leader.IsUp = false;
        }
    }

    public bool SetLag(string nodeName, int lagMs)
    {
        lock (_gate)
        {
            var node = _followers.FirstOrDefault(n => n.Name == nodeName);

            if (node is null)
            {
                return false;
            }

            node.LagMs = lagMs;
            return true;
        }
    }

    public ClusterStatus Status()
    {
        lock (_gate)
        {
            return new ClusterStatus(
                _leader.Name,
                _leader.IsUp,
                _nextLsn - 1,
                _followers
                    .Select(n => new FollowerStatus(n.Name, n.AppliedLsn, n.LagMs, n.IsUp))
                    .ToList());
        }
    }
}
