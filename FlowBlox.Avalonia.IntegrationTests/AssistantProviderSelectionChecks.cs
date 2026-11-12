using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Utilities;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckAssistantProviderSelectionAsync(Action<bool, string> check)
    {
        var first = new OpenAIProvider { Name = "First" };
        var second = new DeepSeekAIProvider { Name = "Second" };
        var configuration = new AssistantConfiguration();
        configuration.Providers.Add(first);
        configuration.Providers.Add(second);
        var window = new Window { Width = 800, Height = 450, ShowInTaskbar = false };
        var resolver = new PropertyControlResolver(window);
        var context = new PropertyControlBindingContext(configuration);
        var selection = resolver.Resolve(
            configuration.GetType().GetProperty(nameof(configuration.SelectedProviderName)),
            configuration, false, null, context).Control;
        var providers = resolver.Resolve(
            configuration.GetType().GetProperty(nameof(configuration.Providers)),
            configuration, false, null, context).Control;
        var panel = new StackPanel();
        panel.Children.Add(providers);
        panel.Children.Add(selection);
        window.Content = panel;
        try
        {
            window.Show();
            await Task.Delay(150);
            window.UpdateLayout();
            check(FindFieldSelectionVisual<ListView>(providers).Items.Count == 2,
                "Assistant configuration renders the stored provider list");
            var selector = FindFieldSelectionVisual<SelectionDropDownControl>(selection);
            check(selector != null && !selector.IsSuggestion && Equals(selector.SelectedValue, "First"),
                "Shared selection dropdown supports provider names and the automatic first selection");
            selector.SetCurrentValue(SelectionDropDownControl.SelectedValueProperty, "Second");
            check(ReferenceEquals(configuration.SelectedProvider, second),
                "Selecting a provider name updates the computed provider used by the assistant");
            configuration.Providers.Add(new OpenAIProvider { Name = "Third" });
            await Task.Delay(50);
            window.UpdateLayout();
            selector = FindFieldSelectionVisual<SelectionDropDownControl>(selection);
            check(selector.ItemsSource.Count(item => item.Value is string) == 3
                && ReferenceEquals(configuration.SelectedProvider, second),
                "Provider list changes refresh available names without replacing the current selection");
            configuration.Providers.Remove(second);
            check(configuration.SelectedProvider == null
                && configuration.ValidateObject().ContainsKey(nameof(configuration.SelectedProviderName)),
                "Removing the selected provider produces a save validation error on the name property");
        }
        finally
        {
            window.Close();
        }
    }
}
