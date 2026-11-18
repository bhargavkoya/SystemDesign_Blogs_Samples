using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Balancer;

[ApiController]
public sealed class ProxyController(
    BackendPool pool,
    IHttpClientFactory httpFactory,
    IOptions<BalancerOptions> options,
    ILogger<ProxyController> log) : ControllerBase
{
    // Catch-all route. Literal routes such as lb/status win over this one.
    // GET only on purpose: GET is safe to retry on another node. POST is not (see the post).
    [HttpGet("{**path}")]
    public async Task<IActionResult> Forward(string? path, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpFactory.CreateClient();
        var tried = new List<BackendNode>(o.MaxAttempts);

        Response.Headers["X-LB-Panic"] = pool.InPanicMode.ToString();

        for (var attempt = 1; attempt <= o.MaxAttempts; attempt++)
        {
            var node = pool.Pick(tried);
            if (node is null) break;

            tried.Add(node);
            node.Enter(); // in-flight counter feeds least-connections

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(o.RequestTimeoutMs);

                var target = new Uri(node.Address, $"{path}{Request.QueryString}");
                using var upstream = await client.GetAsync(target, cts.Token);

                var transient = upstream.StatusCode is HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout;

                if (transient && attempt < o.MaxAttempts)
                {
                    // Passive signal: a failure observed on real traffic counts toward ejection.
                    node.Report(false, o.FailThreshold, o.PassThreshold);
                    continue; // retry on a different node
                }

                // Buffer the body so nothing has been sent to the client yet if we need to retry.
                // The cost is memory per request and no streaming. A production proxy streams.
                var body = await upstream.Content.ReadAsStringAsync(cts.Token);

                Response.Headers["X-Backend"] = node.Address.Authority;
                return new ContentResult
                {
                    StatusCode = (int)upstream.StatusCode,
                    Content = body,
                    ContentType = upstream.Content.Headers.ContentType?.ToString()
                };
            }
            catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !ct.IsCancellationRequested)
            {
                // Connection refused, reset, or per-attempt timeout.
                log.LogWarning("Attempt {Attempt} on {Node} failed: {Reason}", attempt, node.Address, ex.GetType().Name);
                node.Report(false, o.FailThreshold, o.PassThreshold);
            }
            finally
            {
                node.Exit();
            }
        }

        // Out of nodes or out of attempts: fail fast instead of queueing.
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "no backend available", attempts = tried.Count });
    }
}
