using CapacityEstimator.Models;
using CapacityEstimator.Services;
using Microsoft.AspNetCore.Mvc;

namespace CapacityEstimator.Controllers;

[ApiController]
[Route("api/estimates")]
public sealed class EstimatesController(CapacityEstimatorService estimator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<EstimateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<EstimateResponse> Create([FromBody] EstimateRequest request)
    {
        var outcome = estimator.Estimate(request);

        // 400 with the full list of violated assumptions (RFC 7807 problem details).
        return outcome.Response is null
            ? ValidationProblem(new ValidationProblemDetails(outcome.Errors))
            : Ok(outcome.Response);
    }
}
