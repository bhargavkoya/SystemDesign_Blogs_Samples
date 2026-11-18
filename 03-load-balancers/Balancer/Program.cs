using Balancer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BalancerOptions>(builder.Configuration.GetSection(BalancerOptions.Section));
builder.Services.AddSingleton<BackendPool>();
builder.Services.AddHttpClient();               // pooled connections via IHttpClientFactory
builder.Services.AddHostedService<HealthCheckService>();
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
