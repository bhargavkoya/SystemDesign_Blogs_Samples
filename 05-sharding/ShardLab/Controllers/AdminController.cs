using Microsoft.AspNetCore.Mvc;
using ShardLab.Sharding;

namespace ShardLab.Controllers;

[ApiController]
[Route("admin")]
public sealed class AdminController(ShardRouter router) : ControllerBase
{
    [HttpGet("shards")]
    public IActionResult Shards() => Ok(router.Snapshot());

    // Deterministic skewed load. A few heavy users produce most check-ins, so the chunks they hash
    // into grow faster than the rest, even though chunks are spread evenly across shards by count.
    [HttpPost("seed")]
    public IActionResult Seed(
        [FromQuery] int checkins = 30_000,
        [FromQuery] int users = 2_000,
        [FromQuery] int heavyUsers = 10,
        [FromQuery] double heavyShare = 0.6,
        [FromQuery] int venues = 500)
    {
        var random = new Random(42);
        var accepted = 0;
        var rejected = 0;

        for (var i = 0; i < checkins; i++)
        {
            var userId = random.NextDouble() < heavyShare
                ? random.Next(1, heavyUsers + 1)
                : random.Next(heavyUsers + 1, users + 1);

            if (router.Write(userId, random.Next(1, venues + 1)) == WriteOutcome.Accepted) accepted++;
            else rejected++;
        }

        return Ok(new { accepted, rejected, shards = router.Snapshot() });
    }

    [HttpPost("rebalance")]
    public IActionResult Rebalance([FromQuery] double spread = 0.05, [FromQuery] int maxMoves = 60) =>
        Ok(router.Rebalance(spread, maxMoves));

    [HttpPost("shards/{id:int}/down")]
    public IActionResult Down(int id, [FromQuery] bool value = true)
    {
        if (!router.HasShard(id)) return NotFound();
        router.SetDown(id, value);
        return Ok(router.Snapshot());
    }

    [HttpPost("shards/{id:int}/latency")]
    public IActionResult Latency(int id, [FromQuery] int ms = 0)
    {
        if (!router.HasShard(id)) return NotFound();
        router.SetLatency(id, ms);
        return NoContent();
    }
}
