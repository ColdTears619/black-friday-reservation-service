using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Sale.Infrastructure;
using Sale.Infrastructure.Persistence;
using Sale.Api.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();

builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
    {
        Indented = builder.Environment.IsDevelopment()
    };
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Black Friday Sale Reservation API",
        Version = "v1"
    });
});

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Database writes at startup are opt-in until the initialization is approved.
if (builder.Configuration.GetValue<bool>("Database:InitializeOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
        .InitializeAsync(app.Lifetime.ApplicationStopping);
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapGet("/health/live", async (
    HealthCheckService healthChecks,
    CancellationToken cancellationToken) =>
{
    var report = await healthChecks.CheckHealthAsync(cancellationToken);

    var statusCode = report.Status == HealthStatus.Unhealthy
        ? StatusCodes.Status503ServiceUnavailable
        : StatusCodes.Status200OK;

    return Results.Text(
        report.Status.ToString(),
        contentType: "text/plain",
        statusCode: statusCode);
})
.WithName("GetLiveness")
.WithTags("Health")
.Produces<string>(StatusCodes.Status200OK, "text/plain")
.Produces<string>(StatusCodes.Status503ServiceUnavailable, "text/plain");

app.Run();
