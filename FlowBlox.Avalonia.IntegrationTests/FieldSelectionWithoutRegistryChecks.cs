using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Models.FieldSelection;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckFieldSelectionWithoutRegistry(Action<bool, string> check)
    {
        check(FlowBloxRegistryProvider.GetRegistry() == null,
            "Launcher field selection runs without a registry");
        var dialog = new FieldSelectionWindow(new FieldSelectionWindowArgs
        {
            AllowedFieldSelectionModes = Enum.GetValues<FieldSelectionMode>()
        });
        Exception failure = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                var model = (FieldSelectionWindowViewModel)dialog.DataContext;
                var tabs = FindLauncherVisuals<TabControl>(dialog).Single();
                var visibleTabs = tabs.Items.OfType<TabItem>()
                    .Where(tab => tab.Visibility == Visibility.Visible).ToList();
                check(visibleTabs.Count == 2 && model.CanSelectVault && model.CanSelectOptions
                    && model.IsOptionsMode,
                    "Without a registry only Options and Vault tabs are available, with a usable initial selection");
                check(model.FieldRows.Count == 0 && model.ProjectPropertyRows.Count == 0
                    && model.InputFileRows.Count == 0
                    && model.GenerationStrategyDataRows.Count == 0,
                    "Without a registry no project or generation rows are loaded");
                check(model.OptionRows.Count == FlowBloxOptions.GetOptionInstance().GetOptions()
                    .Count(option => option.IsPlaceholderEnabled),
                    "Placeholder-enabled options are loaded independently of the registry");
                model.SelectedTabIndex = 5;
                dialog.UpdateLayout();
                check(model.IsVaultMode && ((TabItem)tabs.SelectedItem).Visibility == Visibility.Visible,
                    "Vault selection remains available without a registry");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                dialog.Close();
            }
        };
        WpfDialogHost.ShowDialog(dialog, _window);
        if (failure != null)
            throw failure;
    }
}
