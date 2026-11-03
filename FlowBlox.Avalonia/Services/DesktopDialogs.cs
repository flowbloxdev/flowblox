using FlowBlox.Core.DependencyInjection;

namespace FlowBlox.AvaloniaDesktop.Services;

internal static class DesktopDialogs
{
    public static IMessageBoxService Messages => FlowBloxServiceLocator.Instance.GetService<IMessageBoxService>();
    public static IFilePickerService Files => FlowBloxServiceLocator.Instance.GetService<IFilePickerService>();
}
