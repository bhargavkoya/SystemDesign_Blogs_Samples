using System.Text.Json.Serialization;
using StampedeLab.Caching;
using StampedeLab.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The "database": 8 concurrent queries, 100 ms each, callers give up after 1 s in the queue.
builder.Services.AddSingleton(new DatabaseOptions(
    MaxConcurrentQueries: 8,
    QueryLatency: TimeSpan.FromMilliseconds(100),
    QueueTimeout: TimeSpan.FromSeconds(1)));

// The cache: 30 s TTL with +/-10% jitter, and stale entries stay usable for 5 more minutes.
builder.Services.AddSingleton(new CacheOptions(
    Ttl: TimeSpan.FromSeconds(30),
    JitterFraction: 0.10,
    StaleWindow: TimeSpan.FromMinutes(5)));

builder.Services.AddSingleton<SlowDatabase>();
builder.Services.AddSingleton<ProductCache>();

var app = builder.Build();
app.MapControllers();
app.Run();
