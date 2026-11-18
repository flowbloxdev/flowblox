using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using FlowBlox.WebServer.Services;

namespace FlowBlox.WebServer.Components;

/// <summary>Refreshes visible dashboard state without replacing user-entered form values.</summary>
public abstract class DashboardPageBase : ComponentBase, IAsyncDisposable
{
    [Inject] public ILogger<DashboardPageBase> PageLogger { get; set; } = default!;
    [Inject] public AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] public WebSessions Sessions { get; set; } = default!;
    [Inject] public NavigationManager SessionNavigation { get; set; } = default!;
    private readonly CancellationTokenSource _stop = new();
    private Task? _poll;

    protected void BeginRefreshing(Func<Task> refresh) => _poll = PollAsync(refresh);

    protected async Task<bool> HasSessionAsync()
    {
        if (Sessions.IsValid((await Authentication.GetAuthenticationStateAsync()).User)) return true;
        SessionNavigation.NavigateTo("/login", forceLoad: true);
        return false;
    }

    private async Task PollAsync(Func<Task> refresh)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                try { await InvokeAsync(async () => { if (await HasSessionAsync()) { await refresh(); StateHasChanged(); } }); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                { PageLogger.LogWarning(exception, "Could not refresh dashboard page state."); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_poll != null) await _poll;
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }
}
