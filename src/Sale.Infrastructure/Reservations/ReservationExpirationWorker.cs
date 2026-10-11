using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sale.Infrastructure.Reservations;

// This only releases expired holds. It does not process purchases or consume ProcessingJobs.
public sealed class ReservationExpirationWorker(
    IServiceScopeFactory scopeFactory, IOptions<ReservationOptions> options,
    ILogger<ReservationExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = logger.BeginScope("Service={Service} WorkerId={WorkerId}",
            "Sale.Worker.Expiration", $"{Environment.MachineName}:{Environment.ProcessId}");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ExpirationPollSeconds));
        try
        {
            do
            {
                try
                {
                    await using var serviceScope = scopeFactory.CreateAsyncScope();
                    var store = serviceScope.ServiceProvider.GetRequiredService<ReservationStore>();
                    var count = await store.ExpireBatchAsync(stoppingToken);
                    if (count > 0) logger.LogInformation(new EventId(1110, "ExpirationBatchCompleted"),
                        "Released {ReservationCount} expired reservations", count);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    logger.LogError(new EventId(1111, "ExpirationBatchFailed"), exception,
                        "Expiration sweep failed; pending holds remain persisted and will be retried on the next sweep");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
