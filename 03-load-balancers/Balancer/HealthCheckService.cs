using Microsoft.Extensions.Options;

namespace Balancer;

public sealed class HealthCheckService(
    BackendPool pool,
    IHttpClientFactory httpFactory,
    IOptions<BalancerOptions> options,
    ILogger<HealthCheckService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(o.HealthIntervalMs));

        try
        {
            do
            {
                // Probe all nodes in parallel so one hung node cannot delay the others' checks.
                await Task.WhenAll(pool.Nodes.Select(n => ProbeAsync(n, o, stoppingToken)));
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task ProbeAsync(BackendNode node, BalancerOptions o, CancellationToken ct)
    {
        var wasHealthy = node.IsHealthy;
        bool ok;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(o.HealthTimeoutMs); // a probe that hangs counts as a failure
            using var response = await httpFactory.CreateClient().GetAsync(new Uri(node.Address, "health"), cts.Token);
            ok = response.IsSuccessStatusCode;
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !ct.IsCancellationRequested)
        {
            ok = false;
        }

        node.Report(ok, o.FailThreshold, o.PassThreshold);

        if (wasHealthy != node.IsHealthy)
        {
            log.LogWarning("{Node} is now {State}", node.Address, node.IsHealthy ? "healthy" : "unhealthy");
        }
    }
}
