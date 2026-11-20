namespace StampedeLab.Data;

public sealed record Product(int Id, string Name, decimal Price, DateTimeOffset LoadedAt);
