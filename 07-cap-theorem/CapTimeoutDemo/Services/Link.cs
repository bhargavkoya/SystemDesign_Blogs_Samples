namespace CapTimeoutDemo.Services;

/// <summary>
/// Simulates the network link between the replica (Node B) and the primary (Node A).
/// When partitioned, messages are dropped: there is no reply and no error, so the caller
/// cannot tell a lost message from a slow one. Only a timeout reveals the problem.
/// </summary>
public sealed class Link
{
    private static readonly TimeSpan RoundTrip = TimeSpan.FromMilliseconds(20);

    private volatile bool _partitioned;

    public bool Partitioned => _partitioned;

    public void SetPartitioned(bool partitioned) => _partitioned = partitioned;

    /// <summary>Ask the primary "is my copy current?" and wait for the answer.</summary>
    public async Task ConfirmAsync(CancellationToken ct)
    {
        if (_partitioned)
        {
            // The request vanishes. Wait forever; the caller's timeout is the only way out.
            await Task.Delay(Timeout.Infinite, ct);
            return;
        }

        await Task.Delay(RoundTrip, ct);
    }
}
