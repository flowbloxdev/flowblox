using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace FlowBlox.AvaloniaDesktop.Interop;

/// <summary>Adapts synchronous UICore/lifecycle contracts to asynchronous desktop dialogs.</summary>
internal static class ModalTask
{
    public static T Run<T>(Func<Task<T>> action)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => Run(action));
        var task = action();
        if (!task.IsCompleted)
        {
            // Pump rendering and input; never block the UI thread on Task.Wait/Result.
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }
}
