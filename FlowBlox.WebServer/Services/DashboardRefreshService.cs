using FlowBlox.Core.Authentication;

namespace FlowBlox.WebServer.Services;

public sealed class DashboardRefreshService(DashboardService dashboard, ILogger<DashboardRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await new FlowBloxAutoLoginExecutor().TryAutoLoginAsync().WaitAsync(TimeSpan.FromSeconds(30), stoppingToken)
            .ContinueWith(task => { if (task.IsFaulted) logger.LogWarning(task.Exception, "Project Space auto-login failed."); }, stoppingToken);
        try { await dashboard.RefreshProjectsAsync(); }
        catch (Exception exception) { logger.LogError(exception, "Dashboard catalog initialization failed."); }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { dashboard.Executions.Refresh(); }
            catch (Exception exception) { logger.LogWarning(exception, "Dashboard execution status refresh failed."); }
        }
    }
}
