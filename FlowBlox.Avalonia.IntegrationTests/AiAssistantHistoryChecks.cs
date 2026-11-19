using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckAssistantEmptyHistory(Action<bool, string> check, string directory)
    {
        var view = new AiAssistantHistoryView { DataContext = new AiAssistantHistoryViewModel() };
        var window = new Window
        {
            Content = view,
            Width = 420,
            Height = 350,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var description = FlowBloxResourceUtil.GetLocalizedString("Empty_Description", typeof(FlowBlox.UICore.Resources.AiAssistantHistoryView));
            check(MenuVisuals<TextBlock>(view).Any(text => text.IsVisible && text.ActualHeight > 0
                    && text.Text.Contains(description, StringComparison.Ordinal)),
                "Empty assistant history renders its localized explanation");
            SaveMenuImage(directory, view, "assistant-empty-history.png");
        }
        finally
        {
            window.Close();
        }
    }
}
