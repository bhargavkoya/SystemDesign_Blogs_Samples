using Microsoft.AspNetCore.Mvc;

namespace Balancer;

[ApiController]
public sealed class StatusController(BackendPool pool) : ControllerBase
{
    [HttpGet("lb/status")]
    public IActionResult Get() => Ok(new
    {
        panic = pool.InPanicMode,
        healthy = pool.HealthyCount,
        total = pool.Nodes.Count,
        nodes = pool.Nodes.Select(n => new { node = n.Address.Authority, healthy = n.IsHealthy, inFlight = n.InFlight })
    });
}
