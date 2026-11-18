using Backend;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<BackendState>();

var app = builder.Build();
app.MapControllers();
app.Run();
