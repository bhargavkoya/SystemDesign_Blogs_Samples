using CapTimeoutDemo.Models;
using CapTimeoutDemo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CapTimeoutDemo.Controllers;

[ApiController]
[Route("read/{operation}/{key}")]
public sealed class ReadsController(ReplicaReader reader, IOptions<PolicyOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string operation, string key, CancellationToken ct)
    {
        // The operation name picks the partition behavior. Nothing else about the read changes.
        if (!options.Value.Operations.TryGetValue(operation, out var policy))
        {
            return NotFound(new { error = $"No policy configured for operation '{operation}'." });
        }

        var outcome = await reader.ReadAsync(key, policy, ct);

        switch (outcome.Kind)
        {
            case ReadKind.Confirmed:
            case ReadKind.Stale:
                var freshness = outcome.Kind == ReadKind.Confirmed ? "confirmed" : "stale";
                Response.Headers["X-Freshness"] = freshness;
                return Ok(new
                {
                    key,
                    value = outcome.Value!.Value,
                    version = outcome.Value.Version,
                    freshness,
                    policy = policy.Mode.ToString(),
                    attempts = outcome.Attempts,
                    elapsedMs = outcome.ElapsedMs
                });

            case ReadKind.NotFound:
                return NotFound(new { error = $"Key '{key}' not found.", elapsedMs = outcome.ElapsedMs });

            default:
                // Refused: we could not confirm the latest value, so we say so instead of guessing.
                Response.Headers.RetryAfter = "1";
                return Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Cannot confirm the latest value",
                    detail: $"Policy {policy.Mode}: gave up after {outcome.Attempts} attempt(s) and {outcome.ElapsedMs} ms.");
        }
    }
}
