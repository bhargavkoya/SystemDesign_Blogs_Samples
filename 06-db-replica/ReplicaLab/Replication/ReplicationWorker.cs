namespace ReplicaLab.Replication;

public sealed class ReplicationWorker(Cluster cluster) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                cluster.ReplicateTick();
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down. Nothing to clean up.
        }
    }
}
