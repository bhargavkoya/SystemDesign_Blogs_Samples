using ReplicaLab.Replication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// One cluster for the whole process. All replication state lives behind it.
builder.Services.AddSingleton<Cluster>();

// Ships the leader's log to followers in the background, like a replication thread.
builder.Services.AddHostedService<ReplicationWorker>();

var app = builder.Build();

app.MapControllers();

app.Run();
