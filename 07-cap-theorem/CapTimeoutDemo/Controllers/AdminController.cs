using CapTimeoutDemo.Services;
using Microsoft.AspNetCore.Mvc;

namespace CapTimeoutDemo.Controllers;

public sealed record WriteRequest(string Value);

/// <summary>Controls for the experiment: write to Node A, cut the link, heal it.</summary>
[ApiController]
[Route("admin")]
public sealed class AdminController(Link link, Cluster cluster) : ControllerBase
{
    [HttpPost("write/{key}")]
    public IActionResult Write(string key, [FromBody] WriteRequest request)
    {
        var written = cluster.Write(key, request.Value);
        return Ok(new
        {
            key,
            written.Value,
            written.Version,
            acceptedBy = "Node A",
            replicatedToNodeB = !link.Partitioned
        });
    }

    [HttpPost("partition")]
    public IActionResult Partition()
    {
        link.SetPartitioned(true);
        return Ok(new { partitioned = true });
    }

    [HttpPost("heal")]
    public IActionResult Heal()
    {
        var replayed = cluster.Heal();
        return Ok(new { partitioned = false, replayedWrites = replayed });
    }
}
