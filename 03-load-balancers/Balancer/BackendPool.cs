using Microsoft.Extensions.Options;

namespace Balancer;

public sealed class BackendPool
{
    private readonly BackendNode[] _nodes;
    private readonly BalancerOptions _options;
    private int _cursor = -1;

    public BackendPool(IOptions<BalancerOptions> options)
    {
        _options = options.Value;
        _nodes = _options.Backends.Select(a => new BackendNode(new Uri(a))).ToArray();
    }

    public IReadOnlyList<BackendNode> Nodes => _nodes;

    public int HealthyCount => _nodes.Count(n => n.IsHealthy);

    // Panic mode: when too few nodes look healthy, the health signal is probably
    // wrong (a network problem, a bad probe) rather than the fleet being gone.
    // Sending traffic to everyone beats sending all of it to the two survivors.
    public bool InPanicMode =>
        _options.PanicThresholdPercent > 0 &&
        HealthyCount * 100 < _nodes.Length * _options.PanicThresholdPercent;

    public BackendNode? Pick(IReadOnlyCollection<BackendNode> alreadyTried)
    {
        var panic = InPanicMode;
        var candidates = _nodes
            .Where(n => (panic || n.IsHealthy) && !alreadyTried.Contains(n))
            .ToArray();

        if (candidates.Length == 0) return null;

        // Rotating start index: round robin uses it directly, least-connections
        // uses it to break ties so the first node in the list is not always favored.
        var start = (int)((uint)Interlocked.Increment(ref _cursor) % (uint)candidates.Length);
        var choice = candidates[start];

        if (_options.Strategy == Strategy.LeastConnections)
        {
            for (var i = 1; i < candidates.Length; i++)
            {
                var next = candidates[(start + i) % candidates.Length];
                if (next.InFlight < choice.InFlight) choice = next;
            }
        }

        return choice;
    }
}
