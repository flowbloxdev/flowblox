using Avalonia.Controls;

namespace FlowBlox.AvaloniaDesktop.Services;

public sealed class OwnerService : IOwnerService
{
    public Window GetCurrentOwner() => global::FlowBlox.AppWindow.AppWindow.Instance;
}
