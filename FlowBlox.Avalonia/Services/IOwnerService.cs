using Avalonia.Controls;

namespace FlowBlox.AvaloniaDesktop.Services;

/// <summary>Provides the application window used to own and center desktop dialogs.</summary>
public interface IOwnerService
{
    Window GetCurrentOwner();
}
