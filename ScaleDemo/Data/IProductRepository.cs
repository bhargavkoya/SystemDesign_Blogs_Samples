using ScaleDemo.Models;

namespace ScaleDemo.Data;

public interface IProductRepository
{
    Task<Product> GetAsync(int id, CancellationToken ct);
    Task UpdatePriceAsync(int id, decimal price, CancellationToken ct);
    int ReadCount { get; }
}
