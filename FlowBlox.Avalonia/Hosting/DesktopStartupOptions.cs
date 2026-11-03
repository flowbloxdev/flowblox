using System;
using System.Threading.Tasks;

namespace FlowBlox.AvaloniaDesktop.Hosting;

/// <summary>Configures a hosted desktop session without changing interactive startup defaults.</summary>
internal sealed class DesktopStartupOptions
{
    public string WorkspaceDirectory { get; init; }
    public bool EnableInteractiveStartup { get; init; } = true;
    public Func<global::FlowBlox.AppWindow.AppWindow, Task> OnStarted { get; init; }
}
