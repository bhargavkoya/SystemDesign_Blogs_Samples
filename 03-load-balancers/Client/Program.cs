using System.Collections.Concurrent;
using System.Diagnostics;

// Usage: dotnet run --project Client -- <url> <totalRequests> <concurrency>
var url = args.Length > 0 ? args[0] : "http://localhost:5100/work";
var total = args.Length > 1 ? int.Parse(args[1]) : 300;
var concurrency = args.Length > 2 ? int.Parse(args[2]) : 20;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

var byBackend = new ConcurrentDictionary<string, int>();
var byStatus = new ConcurrentDictionary<int, int>();
var latenciesMs = new ConcurrentBag<double>();
var panicHeaderSeen = 0;

var wall = Stopwatch.StartNew();

await Parallel.ForEachAsync(
    Enumerable.Range(0, total),
    new ParallelOptions { MaxDegreeOfParallelism = concurrency },
    async (_, ct) =>
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(url, ct);
            latenciesMs.Add(sw.Elapsed.TotalMilliseconds);

            var backend = response.Headers.TryGetValues("X-Backend", out var b) ? b.First() : "(none)";
            byBackend.AddOrUpdate(backend, 1, (_, c) => c + 1);
            byStatus.AddOrUpdate((int)response.StatusCode, 1, (_, c) => c + 1);

            if (response.Headers.TryGetValues("X-LB-Panic", out var p) && p.First() == "True")
                Interlocked.Increment(ref panicHeaderSeen);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            byStatus.AddOrUpdate(0, 1, (_, c) => c + 1); // 0 = no response at all
        }
    });

wall.Stop();

var sorted = latenciesMs.Order().ToArray();
double Percentile(double q) => sorted.Length == 0 ? double.NaN : sorted[(int)Math.Min(sorted.Length - 1, q * sorted.Length)];

Console.WriteLine($"requests={total} concurrency={concurrency} wall={wall.Elapsed.TotalSeconds:F2}s");
Console.WriteLine("status:  " + string.Join("  ", byStatus.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
Console.WriteLine("backend: " + string.Join("  ", byBackend.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
Console.WriteLine($"panic header seen on {panicHeaderSeen} responses");
Console.WriteLine($"latency: p50={Percentile(0.50):F0}ms  p99={Percentile(0.99):F0}ms");
