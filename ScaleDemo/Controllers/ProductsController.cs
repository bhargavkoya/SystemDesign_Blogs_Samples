using Microsoft.AspNetCore.Mvc;
using ScaleDemo.Caching;
using ScaleDemo.Data;
using ScaleDemo.Models;

namespace ScaleDemo.Controllers;

[ApiController]
[Route("products")]
public sealed class ProductsController(IProductRepository repository, SingleFlightCache cache) : ControllerBase
{
    private static string Key(int id) => $"product:{id}";

    // Read path: cache first, database only on a miss.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Product>> Get(int id, CancellationToken ct)
    {
        Product product = await cache.GetOrCreateAsync(
            Key(id),
            token => repository.GetAsync(id, token),
            ttl: TimeSpan.FromSeconds(30),
            ct);

        return product;
    }

    // Same read with no cache, to compare database load.
    [HttpGet("{id:int}/uncached")]
    public async Task<ActionResult<Product>> GetUncached(int id, CancellationToken ct)
        => await repository.GetAsync(id, ct);

    // Write path: update the source of truth first, then evict the cached copy.
    [HttpPut("{id:int}/price")]
    public async Task<IActionResult> UpdatePrice(int id, [FromBody] decimal price, CancellationToken ct)
    {
        await repository.UpdatePriceAsync(id, price, ct);
        cache.Remove(Key(id));
        return NoContent();
    }

    [HttpGet("/stats")]
    public IActionResult Stats() => Ok(new
    {
        databaseReads = repository.ReadCount,
        cacheHits = cache.Hits,
        cacheMisses = cache.Misses
    });
}
