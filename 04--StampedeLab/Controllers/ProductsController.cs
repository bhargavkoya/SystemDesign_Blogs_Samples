using Microsoft.AspNetCore.Mvc;
using StampedeLab.Caching;
using StampedeLab.Data;

namespace StampedeLab.Controllers;

[ApiController]
[Route("products")]
public sealed class ProductsController(ProductCache cache) : ControllerBase
{
    // GET /products/42?mode=ServeStale
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, [FromQuery] CacheMode mode = CacheMode.ServeStale)
    {
        try
        {
            var result = await cache.GetAsync(id, mode);
            Response.Headers["X-Cache"] = result.Outcome.ToString().ToUpperInvariant();
            return Ok(result.Value);
        }
        catch (DatabaseException ex)
        {
            // Nothing cached and the database cannot answer: tell the caller to come back later.
            Response.Headers.RetryAfter = "1";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }
}
