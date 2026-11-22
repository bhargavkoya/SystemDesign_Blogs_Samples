using ShardLab.Options;
using ShardLab.Sharding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ShardingOptions>(builder.Configuration.GetSection(ShardingOptions.Section));

// One router for the whole process: it owns the shards, the chunk directory and the chunk locks.
builder.Services.AddSingleton<ShardRouter>();
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
