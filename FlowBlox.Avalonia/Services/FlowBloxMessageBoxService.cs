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
        => await ShowCoreAsync(message, title, buttons, icon);

    private async Task<MessageBoxResult> ShowCoreAsync(
        string message,
        string title,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        string yesButtonText = null,
        string noButtonText = null,
        MessageBoxResult defaultResult = MessageBoxResult.No)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ShowCoreAsync(
                message, title, buttons, icon, yesButtonText, noButtonText, defaultResult));
        var owner = _dialogs.LastOrDefault() ?? owners.GetCurrentOwner();
        var dialog = new MessageBoxWindow(message, title, buttons, icon, yesButtonText, noButtonText, defaultResult) { Icon = owner.Icon };
        using var scope = new WpfModalScope(owner);
        _dialogs.Add(dialog);
        try
        {
            var result = await dialog.ShowDialog<MessageBoxResult>(owner);
            return result == MessageBoxResult.None
                ? buttons == MessageBoxButtons.YesNo ? defaultResult : MessageBoxResult.Ok
                : result;
        }
        finally { _dialogs.Remove(dialog); }
    }

    public FlowBloxMessageBoxDialogResult ShowMessageBox(
        string message,
        string title,
        string yesButtonText,
        string noButtonText,
        FlowBloxMessageBoxDialogResult defaultResult)
    {
        var result = ModalTask.Run(() => ShowCoreAsync(
            message,
            title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            yesButtonText,
            noButtonText,
            defaultResult == FlowBloxMessageBoxDialogResult.Yes ? MessageBoxResult.Yes : MessageBoxResult.No));
        return result == MessageBoxResult.Yes ? FlowBloxMessageBoxDialogResult.Yes : FlowBloxMessageBoxDialogResult.No;
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
