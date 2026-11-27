using CapTimeoutDemo.Models;
using CapTimeoutDemo.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// One shared link and one shared pair of nodes for the whole process.
builder.Services.AddSingleton<Link>();
builder.Services.AddSingleton<Cluster>();
builder.Services.AddSingleton<ReplicaReader>();

// The partition behavior of every operation lives in configuration, not in code.
builder.Services.Configure<PolicyOptions>(builder.Configuration.GetSection("Policies"));

var app = builder.Build();

app.MapControllers();

app.Run();
