using System.Diagnostics;
using CapTimeoutDemo.Models;

namespace CapTimeoutDemo.Services;

/// <summary>
/// Serves reads from Node B. Before answering, it tries to confirm with Node A that its
/// copy is current. The timeout on that confirmation is where the CAP decision lives:
/// what happens when it fires is chosen per operation by <see cref="OperationPolicy"/>.
/// </summary>
public sealed class ReplicaReader(Link link, Cluster cluster)
{
    public async Task<ReadOutcome> ReadAsync(string key, OperationPolicy policy, CancellationToken requestAborted)
    {
        var clock = Stopwatch.StartNew();

        // Refuse and AnswerStale get one attempt. WaitThenRefuse keeps trying until MaxWaitMs.
        var budget = TimeSpan.FromMilliseconds(
            policy.Mode == ReadMode.WaitThenRefuse ? policy.MaxWaitMs : policy.TimeoutMs);

        var attempts = 0;

        while (true)
        {
            var remaining = budget - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            attempts++;

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            attempt.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(policy.TimeoutMs, remaining.TotalMilliseconds)));

            try
            {
                await link.ConfirmAsync(attempt.Token);

                // Node A answered, so we know the latest value. This is the consistent path.
                var latest = cluster.ReadPrimary(key);
                return new ReadOutcome(
                    latest is null ? ReadKind.NotFound : ReadKind.Confirmed,
                    latest, attempts, clock.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
            {
                // Our own timeout fired, not the client hanging up. We cannot tell whether the
                // message was lost or is just slow. From here, the policy decides.
            }

            if (policy.Mode != ReadMode.WaitThenRefuse)
            {
                break;
            }

            var pause = TimeSpan.FromMilliseconds(policy.RetryDelayMs);
            var left = budget - clock.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(pause < left ? pause : left, requestAborted);
        }

        if (policy.Mode == ReadMode.AnswerStale)
        {
            var local = cluster.ReadReplica(key);
            return new ReadOutcome(
                local is null ? ReadKind.NotFound : ReadKind.Stale,
                local, attempts, clock.ElapsedMilliseconds);
        }

        return new ReadOutcome(ReadKind.Refused, null, attempts, clock.ElapsedMilliseconds);
    }
}
