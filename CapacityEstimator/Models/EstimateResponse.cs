namespace CapacityEstimator.Models;

/// <summary>An assumption the estimator actually used, and where it came from.</summary>
public sealed record UsedAssumption(double Value, string Source);

public sealed record EstimateResponse(
    UsedAssumption PeakFactor,
    UsedAssumption ReplicationFactor,
    double DailyActiveUsers,
    double AverageWritesPerSecond,
    double PeakWritesPerSecond,
    double AverageConcurrentUsers,
    double PeakConcurrentUsers,
    double StoragePerDayBytes,
    double RetainedStorageBytes,
    double RetainedStorageWithReplicasBytes,
    string StoragePerDay,
    string RetainedStorage,
    string RetainedStorageWithReplicas,
    string Precision);

/// <summary>Either a response or a complete list of what is wrong with the request.</summary>
public sealed record EstimateOutcome(
    EstimateResponse? Response,
    IDictionary<string, string[]> Errors);
