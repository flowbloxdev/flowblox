using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.UICore.Operations;
using FlowBlox.UICore.ViewModels.LocalAI;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private static void CheckLocalAISetupInPropertyWindow(Action<bool, string> check, string directory)
    {
        foreach (var culture in new[] { "en-US", "de-DE" })
            CheckLocalAISetupInPropertyWindow(check, directory, culture);
    }

    private static void CheckLocalAISetupInPropertyWindow(Action<bool, string> check, string directory, string culture)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        var block = new OnnxGenAIFlowBlock { Name = "SyntheticLocalAI" };
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(block);
        project.FlowBloxRegistry.RegisterFlowBlock(block);
        var editor = new PropertyWindow(new PropertyWindowArgs(block));
        var openSetup = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var closeSetup = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var setup = Application.Current.Windows.OfType<LocalAISetupWindow>().Single();
                try
                {
                    var model = (LocalAISetupViewModel)setup.DataContext;
                    check(setup.IsVisible && setup.Owner == editor && model.CanConfigure,
                        "Local AI setup opens from a property transaction and is owned by its editor");
                    check(model.Steps.Count == 5 && !model.Steps[1].OverwriteExisting && !model.Steps[2].OverwriteExisting &&
                        model.Steps[3].OverwriteExisting && model.Steps[4].OverwriteExisting,
                        "Setup keeps global overwrite opt-in and defaults block provider/model overwrite to enabled");
                    var steps = MenuVisuals<ListView>(setup).Single();
                    steps.ScrollIntoView(model.Steps.Last());
                    setup.UpdateLayout();
                    // Capture the pane on the window background without the window's opening transition.
                    ((Panel)setup.Content).Background = System.Windows.Media.Brushes.White;
                    SaveMenuImage(directory, (FrameworkElement)setup.Content, $"local-ai-setup-in-property-window-{culture}.png");
                }
                finally
                {
                    setup.Close();
                }
            }));
            try
            {
                var action = new OnnxFlowBlockUIActions(block);
                check(action.CanConfigureLocalAI(), "Local AI setup is enabled for the original action target during a property transaction");
                action.ConfigureLocalAI();
            }
            finally
            {
                closeSetup.Abort();
                editor.Close();
            }
        }));
        try
        {
            editor.ShowDialog();
            check(project.InputFiles.Count == 0 && !project.FlowBloxRegistry.GetUserFields().Any(),
                "Closing the setup without configuring leaves global resources unchanged");
        }
        finally
        {
            openSetup.Abort();
            editor.Close();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
