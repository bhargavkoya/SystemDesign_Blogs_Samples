using Microsoft.AspNetCore.Mvc;
using ReplicaLab.Models;
using ReplicaLab.Replication;

namespace ReplicaLab.Controllers;

[ApiController]
public sealed class KvController(Cluster cluster) : ControllerBase
{
    // Writes always go to the leader. The response carries the LSN,
    // which the client sends back as minLsn to get read-your-writes.
    [HttpPut("kv/{key}")]
    public IActionResult Put(string key, [FromBody] PutRequest body)
    {
        try
        {
            var lsn = cluster.Write(key, body.Value);
            return Ok(new { lsn });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    // minLsn = 0 means "any replica will do", which is how you get stale reads.
    // waitMs is the budget to wait for a fresh follower before using the leader.
    [HttpGet("kv/{key}")]
    public async Task<IActionResult> Get(
        string key,
        [FromQuery] long minLsn = 0,
        [FromQuery] int waitMs = 50,
        CancellationToken ct = default)
    {
        try
        {
            var result = await cluster.ReadAsync(key, minLsn, TimeSpan.FromMilliseconds(waitMs), ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }
}
