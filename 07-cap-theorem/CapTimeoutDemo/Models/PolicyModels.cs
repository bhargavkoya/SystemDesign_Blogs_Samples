namespace CapTimeoutDemo.Models;

/// <summary>What a read does when it cannot confirm it is current.</summary>
public enum ReadMode
{
    /// <summary>One attempt, then fail. Consistency over availability.</summary>
    Refuse,

    /// <summary>One attempt, then answer from the local replica. Availability over consistency.</summary>
    AnswerStale,

    /// <summary>Retry until a deadline, then fail. Consistency, paid for in latency.</summary>
    WaitThenRefuse
}

/// <summary>The partition behavior of one operation, bound from configuration.</summary>
public sealed class OperationPolicy
{
    public ReadMode Mode { get; set; } = ReadMode.Refuse;

    /// <summary>How long a single attempt to confirm with the primary may take.</summary>
    public int TimeoutMs { get; set; } = 300;

    /// <summary>Only used by WaitThenRefuse: the total time the caller will wait.</summary>
    public int MaxWaitMs { get; set; } = 2000;

    /// <summary>Only used by WaitThenRefuse: pause between attempts.</summary>
    public int RetryDelayMs { get; set; } = 200;
}

public sealed class PolicyOptions
{
    public Dictionary<string, OperationPolicy> Operations { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed record VersionedValue(string Value, int Version);

public enum ReadKind
{
    Confirmed,
    Stale,
    NotFound,
    Refused
}

public sealed record ReadOutcome(
    ReadKind Kind,
    VersionedValue? Value,
    int Attempts,
    long ElapsedMs);
