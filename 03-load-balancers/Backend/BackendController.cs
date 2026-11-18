using Microsoft.AspNetCore.Mvc;

namespace Backend;

[ApiController]
public sealed class BackendController(BackendState state, IConfiguration config) : ControllerBase
{
    // Set per instance from the command line: --Backend:Name=b1 --Backend:DelayMs=20
    private readonly string _name = config["Backend:Name"] ?? "backend";
    private readonly int _delayMs = config.GetValue("Backend:DelayMs", 20);

    [HttpGet("work")]
    public async Task<IActionResult> Work(CancellationToken ct)
    {
        await Task.Delay(_delayMs, ct); // stands in for real work
        return Ok(new { backend = _name });
    }

    [HttpGet("health")]
    public IActionResult Health() =>
        state.HealthFailing ? StatusCode(StatusCodes.Status503ServiceUnavailable, "unhealthy") : Ok("ok");

    // POST /admin/health/false makes the probe fail; POST /admin/health/true restores it.
    [HttpPost("admin/health/{passing:bool}")]
    public IActionResult SetHealth(bool passing)
    {
        state.HealthFailing = !passing;
        return Ok(new { backend = _name, healthPassing = passing });
    }
}
