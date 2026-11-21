using FlowBlox.UICore.Enums;

namespace FlowBlox.UICore.Interfaces
{
    public interface IFlowBloxMessageBoxService
    {
        FlowBloxMessageBoxDialogResult ShowMessageBox(string message, string title, FlowBloxMessageBoxTypes flowBloxMessageBoxType);

        FlowBloxMessageBoxDialogResult ShowMessageBox(
            string message,
            string title,
            string yesButtonText,
            string noButtonText,
            FlowBloxMessageBoxDialogResult defaultResult)
            => ShowMessageBox(message, title, FlowBloxMessageBoxTypes.Question);
    }
}
