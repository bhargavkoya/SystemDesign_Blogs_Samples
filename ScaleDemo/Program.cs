using ScaleDemo.Caching;
using ScaleDemo.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IProductRepository, SlowProductRepository>();
builder.Services.AddSingleton<SingleFlightCache>();

var app = builder.Build();

app.MapControllers();
app.Run();
