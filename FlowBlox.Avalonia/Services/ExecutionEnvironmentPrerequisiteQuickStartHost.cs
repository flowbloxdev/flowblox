using System;
using System.Linq;
using Avalonia.Threading;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.UICore.PopUp.Provider;
using System.Windows;

namespace FlowBlox.AvaloniaDesktop.Services;

/// <summary>
/// Shows existing WPF Quick Starts for failures from the designer and separate runners.
/// </summary>
public sealed class ExecutionEnvironmentPrerequisiteQuickStartHost : IExecutionEnvironmentPrerequisiteQuickStartHost
{
    private readonly IOwnerService _owners;

    public ExecutionEnvironmentPrerequisiteQuickStartHost(IOwnerService owners)
    {
        _owners = owners;
    }

    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public void Show(IComponentPopupProvider provider)
    {
        var window = provider.CreateQuickStartWindow();
        if (window == null)
            return;

        var wpfOwner = Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(candidate => candidate.IsActive && candidate.IsVisible);
        if (wpfOwner != null)
        {
            window.Owner = wpfOwner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowDialog();
            return;
        }

        var owner = _owners.GetCurrentOwner();
        if (owner?.IsVisible == true)
            WpfDialogHost.ShowDialog(window, owner);
    }
}
