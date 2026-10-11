using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sale.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddReservationExpirationWorker();

using var host = builder.Build();

await host.RunAsync();
