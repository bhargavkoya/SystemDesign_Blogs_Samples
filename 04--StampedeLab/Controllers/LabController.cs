using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StampedeLab.Caching;
using StampedeLab.Data;

namespace StampedeLab.Controllers;

public enum LabScenario
{
    /// <summary>A hot key's TTL elapses while N clients are asking for it.</summary>
    Expiry,

    /// <summary>The same expiry, but the database is down when it happens.</summary>
    Outage,
}

public sealed record LabReport(
    CacheMode Mode,
    LabScenario Scenario,
    int Clients,
    int Succeeded,
    int Failed,
    int ServedFresh,
    int ServedStale,
    int ServedAfterMiss,
    long DbQueriesStarted,
    long DbQueriesFailed,
    int DbPeakConcurrentQueries,
    double LatencyP50Ms,
    double LatencyP95Ms,
    double LatencyMaxMs);

[ApiController]
[Route("lab")]
public sealed class LabController(ProductCache cache, SlowDatabase db) : ControllerBase
{
    private const int HotProductId = 42;

    // The cache and database are singletons, so two runs at once would corrupt each other's numbers.
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    // POST /lab/run?mode=Naive&scenario=Expiry&clients=200
    [HttpPost("run")]
    public async Task<ActionResult<LabReport>> Run(
        [FromQuery] CacheMode mode,
        [FromQuery] LabScenario scenario = LabScenario.Expiry,
        [FromQuery] int clients = 200)
    {
        if (clients is < 1 or > 2000)
            return BadRequest(new { error = "clients must be between 1 and 2000" });

        if (!await RunLock.WaitAsync(0))
            return Conflict(new { error = "another lab run is in progress" });

        try
        {
            // 1. Start from a known state: empty cache, healthy database, then warm the hot key once.
            db.Failing = false;
            cache.Clear();
            await cache.GetAsync(HotProductId, CacheMode.Naive);
            db.ResetStats();

            // 2. The moment under test: the TTL elapses (and, in the outage scenario, the database dies).
            cache.ExpireNow(HotProductId);
            db.Failing = scenario == LabScenario.Outage;

            // 3. N clients ask for the hot key at the same time.
            var calls = Enumerable.Range(0, clients).Select(_ => TimedCallAsync(mode)).ToArray();
            var results = await Task.WhenAll(calls);

            // 4. Let a background refresh finish so its database attempt is counted.
            await cache.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(3));

            var stats = db.Snapshot();
            var latencies = results.Select(r => r.Ms).Order().ToArray();

            return Ok(new LabReport(
                mode, scenario, clients,
                Succeeded: results.Count(r => r.Ok),
                Failed: results.Count(r => !r.Ok),
                ServedFresh: results.Count(r => r.Outcome == CacheOutcome.Hit),
                ServedStale: results.Count(r => r.Outcome == CacheOutcome.Stale),
                ServedAfterMiss: results.Count(r => r.Outcome == CacheOutcome.Miss),
                stats.QueriesStarted, stats.QueriesFailed, stats.PeakConcurrentQueries,
                Math.Round(Percentile(latencies, 0.50), 1),
                Math.Round(Percentile(latencies, 0.95), 1),
                Math.Round(latencies[^1], 1)));
        }
        finally
        {
            db.Failing = false;
            RunLock.Release();
        }
    }

    private async Task<(bool Ok, CacheOutcome? Outcome, double Ms)> TimedCallAsync(CacheMode mode)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await cache.GetAsync(HotProductId, mode);
            return (true, result.Outcome, sw.Elapsed.TotalMilliseconds);
        }
        catch (DatabaseException)
        {
            return (false, null, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
}
