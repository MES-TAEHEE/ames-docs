using AMES.Data.Connection;
using AMES.Data.Repositories;

namespace AMES.Api.Workers;

// The worker runs in the API only, never in a PDA client or a web page render.
public sealed class LineReplenishmentWorker(AmesConnectionFactory factory,
    IConfiguration configuration, ILogger<LineReplenishmentWorker> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!configuration.GetValue("WarehouseReplenishment:Enabled", true)) continue;
                try
                {
                    var result = new WarehouseRepository(factory).GenerateLinePickingOrders();
                    log.LogInformation("Line replenishment: {Orders} orders, {Lines} materials, {Invalid} invalid requests",
                        result.Orders, result.Lines, result.InvalidRequests);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Line replenishment failed; will retry on the next 10-minute tick");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
