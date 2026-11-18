namespace Balancer;

public sealed class BackendNode(Uri address)
{
    private readonly object _gate = new();
    private int _failures;
    private int _successes;
    private int _inFlight;
    private volatile bool _healthy = true;

    public Uri Address { get; } = address;
    public bool IsHealthy => _healthy;
    public int InFlight => Volatile.Read(ref _inFlight);

    public void Enter() => Interlocked.Increment(ref _inFlight);
    public void Exit() => Interlocked.Decrement(ref _inFlight);

    // Flip state only after N consecutive results in the same direction.
    // One slow probe should not eject a node, and one lucky probe should not readmit it.
    public void Report(bool ok, int failThreshold, int passThreshold)
    {
        lock (_gate)
        {
            if (ok)
            {
                _failures = 0;
                _successes = Math.Min(_successes + 1, passThreshold);
                if (_successes >= passThreshold) _healthy = true;
            }
            else
            {
                _successes = 0;
                _failures = Math.Min(_failures + 1, failThreshold);
                if (_failures >= failThreshold) _healthy = false;
            }
        }
    }
}
