namespace Balancer;

public enum Strategy
{
    RoundRobin,
    LeastConnections
}

public sealed class BalancerOptions
{
    public const string Section = "Balancer";

    public List<string> Backends { get; set; } = [];
    public Strategy Strategy { get; set; } = Strategy.LeastConnections;

    // Active health checks.
    public int HealthIntervalMs { get; set; } = 1000;
    public int HealthTimeoutMs { get; set; } = 500;
    public int FailThreshold { get; set; } = 2;   // consecutive failures before ejecting a node
    public int PassThreshold { get; set; } = 2;   // consecutive passes before readmitting it

    // If fewer than this percentage of nodes are healthy, ignore health state and use all nodes.
    // 0 disables panic mode.
    public int PanicThresholdPercent { get; set; } = 50;

    // Retries.
    public int MaxAttempts { get; set; } = 2;     // 1 original try + 1 retry, on a different node
    public int RequestTimeoutMs { get; set; } = 2000;
}
