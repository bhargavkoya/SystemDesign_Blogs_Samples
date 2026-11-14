using CapacityEstimator.Options;
using CapacityEstimator.Services;

var builder = WebApplication.CreateBuilder(args);

// Decision 4: validate configuration at startup. A bad MaxPeakFactor should stop the
// process at deploy time, not surface as a wrong estimate in a design review.
builder.Services
    .AddOptions<EstimatorOptions>()
    .BindConfiguration(EstimatorOptions.SectionName)
    .Validate(o => EstimatorOptions.Validate(o) is null, "Invalid Estimator configuration.")
    .ValidateOnStart();

builder.Services.AddSingleton<CapacityEstimatorService>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
