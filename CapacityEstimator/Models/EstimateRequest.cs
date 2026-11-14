namespace CapacityEstimator.Models;

/// <summary>
/// Every assumption is nullable on purpose. A missing value is reported as an error
/// instead of silently becoming 0, because a silent zero produces a confident wrong answer.
/// Units are in the property names: bytes, seconds, a ratio between 0 and 1.
/// </summary>
public sealed record EstimateRequest
{
    public long? MonthlyActiveUsers { get; init; }
    public double? DailyActiveRatio { get; init; }
    public double? WritesPerUserPerDay { get; init; }
    public double? BytesPerWrite { get; init; }
    public double? MediaRatio { get; init; }
    public double? BytesPerMedia { get; init; }
    public double? RetentionYears { get; init; }
    public double? SessionsPerUserPerDay { get; init; }
    public double? SessionSeconds { get; init; }

    // Optional: fall back to configured defaults, and the response says so.
    public double? PeakFactor { get; init; }
    public int? ReplicationFactor { get; init; }
}
