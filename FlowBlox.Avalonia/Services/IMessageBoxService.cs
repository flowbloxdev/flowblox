using System.Threading.Tasks;

namespace FlowBlox.AvaloniaDesktop.Services;

public enum MessageBoxButtons { Ok, YesNo }
public enum MessageBoxIcon { Information, Warning, Error, Question }
public enum MessageBoxResult { None, Ok, Yes, No }

public interface IMessageBoxService
{
    Task<MessageBoxResult> ShowAsync(string message, string title,
        MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.Information);

    // Retained for the existing synchronous UICore contracts and project lifecycle.
    MessageBoxResult Show(string message, string title,
        MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.Information);
}
