namespace CapacityEstimator.Options;

/// <summary>
/// Policy knobs for the estimator. These are the numbers a team argues about,
/// so they live in configuration and are validated at startup.
/// </summary>
public sealed class EstimatorOptions
{
    public const string SectionName = "Estimator";

    /// <summary>Used when the caller does not supply a peak factor. Peak QPS = average QPS x this.</summary>
    public double DefaultPeakFactor { get; init; } = 2.0;

    /// <summary>
    /// Requests above this are rejected, not clamped. A 40x "peak" is a different
    /// problem (a launch, a flash sale) and deserves its own estimate.
    /// </summary>
    public double MaxPeakFactor { get; init; } = 10.0;

    /// <summary>Used when the caller does not supply a replication factor.</summary>
    public int DefaultReplicationFactor { get; init; } = 3;

    /// <summary>Returns an error message, or null when the options are usable.</summary>
    public static string? Validate(EstimatorOptions o)
    {
        if (o.DefaultPeakFactor < 1.0)
            return "Estimator:DefaultPeakFactor must be >= 1 (peak cannot be below average).";
        if (o.MaxPeakFactor < o.DefaultPeakFactor)
            return "Estimator:MaxPeakFactor must be >= Estimator:DefaultPeakFactor.";
        if (o.DefaultReplicationFactor is < 1 or > 10)
            return "Estimator:DefaultReplicationFactor must be between 1 and 10.";
        return null;
    }
}
