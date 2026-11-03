using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using FlowBlox.AvaloniaDesktop.Dialogs;
using FlowBlox.AvaloniaDesktop.Interop;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;

namespace FlowBlox.AvaloniaDesktop.Services;

public sealed class FlowBloxMessageBoxService(IOwnerService owners) : IMessageBoxService, IFlowBloxMessageBoxService
{
    private readonly List<MessageBoxWindow> _dialogs = new();

    public MessageBoxResult Show(string message, string title, MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.Information) => ModalTask.Run(() => ShowAsync(message, title, buttons, icon));

    public async Task<MessageBoxResult> ShowAsync(string message, string title, MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(message, title, buttons, icon));
        var owner = _dialogs.LastOrDefault() ?? owners.GetCurrentOwner();
        var dialog = new MessageBoxWindow(message, title, buttons, icon) { Icon = owner.Icon };
        using var scope = new WpfModalScope(owner);
        _dialogs.Add(dialog);
        try
        {
            var result = await dialog.ShowDialog<MessageBoxResult>(owner);
            return result == MessageBoxResult.None
                ? buttons == MessageBoxButtons.YesNo ? MessageBoxResult.No : MessageBoxResult.Ok
                : result;
        }
        finally { _dialogs.Remove(dialog); }
    }

    public FlowBloxMessageBoxDialogResult ShowMessageBox(string message, string title, FlowBloxMessageBoxTypes type)
    {
        var icon = type switch
        {
            FlowBloxMessageBoxTypes.Error => MessageBoxIcon.Error,
            FlowBloxMessageBoxTypes.Warning => MessageBoxIcon.Warning,
            FlowBloxMessageBoxTypes.Question => MessageBoxIcon.Question,
            _ => MessageBoxIcon.Information
        };
        return Show(message, title, type == FlowBloxMessageBoxTypes.Question ? MessageBoxButtons.YesNo : MessageBoxButtons.Ok, icon) switch
        {
            MessageBoxResult.Yes => FlowBloxMessageBoxDialogResult.Yes,
            MessageBoxResult.No => FlowBloxMessageBoxDialogResult.No,
            _ => FlowBloxMessageBoxDialogResult.OK
        };
    }
}
