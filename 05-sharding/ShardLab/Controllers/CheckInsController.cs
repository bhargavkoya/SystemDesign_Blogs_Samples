using Microsoft.AspNetCore.Mvc;
using ShardLab.Models;
using ShardLab.Sharding;

namespace ShardLab.Controllers;

[ApiController]
public sealed class CheckInsController(ShardRouter router) : ControllerBase
{
    [HttpPost("checkins")]
    public IActionResult Create([FromBody] CheckInRequest request)
    {
        try
        {
            return router.Write(request.UserId, request.VenueId) == WriteOutcome.Accepted
                ? Accepted()
                : StatusCode(StatusCodes.Status507InsufficientStorage, new { error = "Shard is at capacity." });
        }
        catch (ShardUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpGet("users/{userId:long}/checkins")]
    public IActionResult ForUser(long userId)
    {
        try
        {
            var items = router.ReadUser(userId);
            return Ok(new { count = items.Count, items });
        }
        catch (ShardUnavailableException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpGet("venues/{venueId:long}/checkins")]
    public async Task<IActionResult> ForVenue(
        long venueId, [FromQuery] ScatterPolicy policy = ScatterPolicy.Partial, CancellationToken ct = default)
    {
        try
        {
            var result = await router.QueryVenueAsync(venueId, policy, ct);
            return Ok(new
            {
                complete = result.Complete,
                missingShards = result.MissingShards,
                count = result.Items.Count,
                newest = result.Items.Take(3)
            });
        }
        catch (ScatterIncompleteException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = ex.Message, missingShards = ex.MissingShards });
        }
    }
}
