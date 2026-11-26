using Microsoft.AspNetCore.Mvc;
using ReplicaLab.Replication;

namespace ReplicaLab.Controllers;

[ApiController]
[Route("admin")]
public sealed class AdminController(Cluster cluster) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Ok(cluster.Status());

    [HttpPost("lag/{node}/{ms:int}")]
    public IActionResult SetLag(string node, int ms)
    {
        if (!cluster.SetLag(node, ms))
        {
            return NotFound();
        }

        return Ok(cluster.Status());
    }

    [HttpPost("kill-leader")]
    public IActionResult KillLeader()
    {
        cluster.KillLeader();
        return Ok(cluster.Status());
    }

    [HttpPost("failover")]
    public IActionResult Failover([FromQuery] bool force = false)
    {
        var result = cluster.Failover(force);

        if (!result.Promoted)
        {
            return Conflict(result);
        }

        return Ok(result);
    }
}
