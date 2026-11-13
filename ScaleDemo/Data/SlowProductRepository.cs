using System.Collections.Concurrent;
using ScaleDemo.Models;

namespace ScaleDemo.Data;

// Stands in for the database tier. Every read costs ~200 ms and is counted,
// so the effect of the cache on database load is visible.
public sealed class SlowProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<int, Product> _rows = new();
    private int _readCount;

    public int ReadCount => Volatile.Read(ref _readCount);

    public async Task<Product> GetAsync(int id, CancellationToken ct)
    {
        Interlocked.Increment(ref _readCount);
        await Task.Delay(200, ct); // simulated query latency

        return _rows.GetOrAdd(id, key => new Product(key, $"Product {key}", 9.99m, Version: 1));
    }

    public async Task UpdatePriceAsync(int id, decimal price, CancellationToken ct)
    {
        await Task.Delay(50, ct); // simulated write
        _rows.AddOrUpdate(
            id,
            key => new Product(key, $"Product {key}", price, Version: 2),
            (_, current) => current with { Price = price, Version = current.Version + 1 });
    }
}
