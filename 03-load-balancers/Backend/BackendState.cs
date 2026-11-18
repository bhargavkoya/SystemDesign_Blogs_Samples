namespace Backend;

// Shared, mutable switch that lets us make /health fail while /work keeps working.
// That combination is the interesting one: the instance can serve traffic,
// but the load balancer's health probe says it cannot.
public sealed class BackendState
{
    private int _healthFailing; // 0 = health passes, 1 = health fails

    public bool HealthFailing
    {
        get => Volatile.Read(ref _healthFailing) == 1;
        set => Volatile.Write(ref _healthFailing, value ? 1 : 0);
    }
}
