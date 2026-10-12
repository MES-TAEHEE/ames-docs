using AMES.Api.Auth;
using AMES.Data.Repositories;

namespace AMES.Api.Workers;

public sealed class SessionCleanupWorker(TokenStore tokens, PopSessionRepository sessions, ILogger<SessionCleanupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                tokens.PruneExpired();
                sessions.CloseExpiredSessions();
            }
            catch (Exception ex) { log.LogError(ex, "Expired session cleanup failed"); }
        }
    }
}
