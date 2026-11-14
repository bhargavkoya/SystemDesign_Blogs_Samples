using System.Text.Json;
using CapacityEstimator.Models;
using CapacityEstimator.Options;
using Microsoft.Extensions.Options;

namespace CapacityEstimator.Services;

public sealed class CapacityEstimatorService(IOptions<EstimatorOptions> options)
{
    private const double SecondsPerDay = 86_400;
    private const double DaysPerYear = 365;

    // Two significant figures. An input like "20 million users" is not known to
    // 7 digits, so the output must not pretend it is.
    private const int SignificantDigits = 2;

    private readonly EstimatorOptions _options = options.Value;

    public EstimateOutcome Estimate(EstimateRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        // Decision 1: collect EVERY problem. Failing on the first one makes the caller
        // fix and resubmit five times, and people give up and guess instead.
        var mau = Require(request.MonthlyActiveUsers, nameof(request.MonthlyActiveUsers), 1, 10_000_000_000, errors);
        var dauRatio = Require(request.DailyActiveRatio, nameof(request.DailyActiveRatio), 0.0001, 1, errors);
        var writes = Require(request.WritesPerUserPerDay, nameof(request.WritesPerUserPerDay), 0.0001, 100_000, errors);
        var bytesPerWrite = Require(request.BytesPerWrite, nameof(request.BytesPerWrite), 1, 1_000_000_000, errors);
        var mediaRatio = Require(request.MediaRatio, nameof(request.MediaRatio), 0, 1, errors);
        var bytesPerMedia = Require(request.BytesPerMedia, nameof(request.BytesPerMedia), 0, 1_000_000_000_000, errors);
        var years = Require(request.RetentionYears, nameof(request.RetentionYears), 0.0001, 100, errors);
        var sessions = Require(request.SessionsPerUserPerDay, nameof(request.SessionsPerUserPerDay), 0.0001, 1_000, errors);
        var sessionSeconds = Require(request.SessionSeconds, nameof(request.SessionSeconds), 1, SecondsPerDay, errors);

        if (mediaRatio > 0 && bytesPerMedia == 0)
            Add(errors, nameof(request.BytesPerMedia), "MediaRatio is above 0 but BytesPerMedia is 0. One of them is wrong.");

        // Decision 2: an oversized peak factor is REJECTED, never clamped.
        // Clamping would return a plausible number for a question nobody asked.
        double peak, replicas;
        string peakSource, replicaSource;

        if (request.PeakFactor is { } suppliedPeak)
        {
            peak = suppliedPeak;
            peakSource = "request";
            if (peak < 1 || peak > _options.MaxPeakFactor)
                Add(errors, nameof(request.PeakFactor),
                    $"Must be between 1 and {_options.MaxPeakFactor} (configured cap). " +
                    "A larger spike needs its own estimate, not a bigger multiplier.");
        }
        else
        {
            peak = _options.DefaultPeakFactor;
            peakSource = "configured default";
        }

        if (request.ReplicationFactor is { } suppliedReplicas)
        {
            replicas = suppliedReplicas;
            replicaSource = "request";
            if (suppliedReplicas is < 1 or > 10)
                Add(errors, nameof(request.ReplicationFactor), "Must be between 1 and 10.");
        }
        else
        {
            replicas = _options.DefaultReplicationFactor;
            replicaSource = "configured default";
        }

        if (errors.Count > 0)
            return new EstimateOutcome(null, errors);

        // ---- The arithmetic. Everything below is the whole "estimation" chapter. ----

        var dau = mau * dauRatio;

        // Writes per second: total daily writes spread evenly over the day.
        var avgWritesPerSecond = dau * writes / SecondsPerDay;
        var peakWritesPerSecond = avgWritesPerSecond * peak;

        // Concurrent users by Little's law: L = lambda x W.
        // lambda = session starts per second, W = how long a session lasts.
        var sessionStartsPerSecond = dau * sessions / SecondsPerDay;
        var avgConcurrent = sessionStartsPerSecond * sessionSeconds;
        var peakConcurrent = avgConcurrent * peak;

        // Storage: text for every write, plus media for the fraction that has it.
        var bytesPerWriteIncludingMedia = bytesPerWrite + mediaRatio * bytesPerMedia;
        var storagePerDay = dau * writes * bytesPerWriteIncludingMedia;
        var retained = storagePerDay * DaysPerYear * years;
        var retainedWithReplicas = retained * replicas;

        var response = new EstimateResponse(
            PeakFactor: new UsedAssumption(peak, peakSource),
            ReplicationFactor: new UsedAssumption(replicas, replicaSource),
            DailyActiveUsers: Round(dau),
            AverageWritesPerSecond: Round(avgWritesPerSecond),
            PeakWritesPerSecond: Round(peakWritesPerSecond),
            AverageConcurrentUsers: Round(avgConcurrent),
            PeakConcurrentUsers: Round(peakConcurrent),
            StoragePerDayBytes: Round(storagePerDay),
            RetainedStorageBytes: Round(retained),
            RetainedStorageWithReplicasBytes: Round(retainedWithReplicas),
            StoragePerDay: FormatBytes(storagePerDay),
            RetainedStorage: FormatBytes(retained),
            RetainedStorageWithReplicas: FormatBytes(retainedWithReplicas),
            Precision: $"{SignificantDigits} significant figures; decimal units (1 TB = 10^12 bytes)");

        return new EstimateOutcome(response, errors);
    }

    private static double Require(double? value, string name, double min, double max,
        Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            Add(errors, name, "Required. Missing assumptions are not defaulted to 0.");
            return 0;
        }

        if (double.IsNaN(value.Value) || value < min || value > max)
        {
            Add(errors, name, $"Must be between {min:G} and {max:G}.");
            return 0;
        }

        return value.Value;
    }

    // Keys are camelCased so an error names the same field the caller sent in the JSON body.
    private static void Add(Dictionary<string, string[]> errors, string key, string message)
    {
        key = JsonNamingPolicy.CamelCase.ConvertName(key);
        errors[key] = errors.TryGetValue(key, out var existing) ? [.. existing, message] : [message];
    }

    // Round to N significant figures: 3472.2 -> 3500, 0.0123 -> 0.012.
    private static double Round(double value)
    {
        if (value == 0 || !double.IsFinite(value)) return value;
        var exponent = (int)Math.Floor(Math.Log10(Math.Abs(value))) - (SignificantDigits - 1);

        // Powers of ten are exact in a double only when they are whole numbers, so
        // multiply by a whole power when the result is large and divide by one when it
        // is small. Multiplying by 10^-5 is what printed 7999999.999999999.
        return exponent >= 0
            ? Math.Round(value / Math.Pow(10, exponent)) * Math.Pow(10, exponent)
            : Math.Round(value * Math.Pow(10, -exponent)) / Math.Pow(10, -exponent);
    }

    // Decision 3: decimal (SI) units, labelled. Powers of 1000, not 1024.
    // The gap is about 2.4% at KB, 10% at TB, 13% at PB, which is smaller than the
    // error in any assumption. The real bug is mixing the two in one calculation.
    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var value = Round(bytes);
        var i = 0;
        while (value >= 1000 && i < units.Length - 1)
        {
            value /= 1000;
            i++;
        }

        return $"{value:G3} {units[i]}";
    }
}
