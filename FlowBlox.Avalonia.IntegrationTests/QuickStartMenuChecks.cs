using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using FlowBlox.Core.Models.FlowBlocks.AIRemote;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckQuickStartMenu(Action<bool, string> check, string directory)
    {
        var providers = FlowBloxServiceLocator.Instance.GetServices<IComponentPopupProvider>()
            .Where(provider => provider.IsQuickStart)
            .GroupBy(provider => provider.OptionKey)
            .Select(group => group.First())
            .ToArray();
        var menu = _window.Menus.SelectMany(item => item.DropDownItems)
            .Single(item => item.DropDownItems.Count == providers.Length
                && providers.All(provider => item.DropDownItems.Any(entry => Equals(entry.Tag, provider.TargetType))));

        foreach (var provider in providers)
        {
            var entry = menu.DropDownItems.Single(item => Equals(item.Tag, provider.TargetType));
            var opened = false;
            var close = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var popup = Application.Current.Windows.OfType<ComponentPopupWindow>().Single();
                opened = popup.IsVisible && !string.IsNullOrWhiteSpace(popup.Title);
                if (provider.TargetType == typeof(AIPromptFlowBlock))
                    CheckAIPromptQuickStartNavigation(popup, directory, check);

                popup.Close();
            }));
            try
            {
                entry.Execute(null);
            }
            finally
            {
                close.Abort();
            }

            check(opened, $"Help opens registered Quick Start without a configured target: {provider.TargetType.Name}");
        }
    }

    private static void CheckAIPromptQuickStartNavigation(
        ComponentPopupWindow popup,
        string directory,
        Action<bool, string> check)
    {
        var image = (Image)popup.FindName("StepImage");
        var next = (Button)popup.FindName("NextButton");
        var previous = (Button)popup.FindName("PreviousButton");
        var firstImage = image.Source;

        for (var step = 1; step <= 2; step++)
        {
            popup.UpdateLayout();
            check(image.Source != null && image.ActualWidth > 0 && image.ActualHeight > 0,
                $"AI Prompt Quick Start renders its illustration on step {step}");
            SaveMenuImage(directory, (FrameworkElement)popup.Content, $"ai-prompt-quick-start-{step}.png");

            if (step < 2)
            {
                var currentImage = image.Source;
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!ReferenceEquals(image.Source, currentImage),
                    $"AI Prompt Quick Start advances from step {step}");
            }
        }

        previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(image.Source != null && image.Source.Width == firstImage.Width && !previous.IsVisible,
            "AI Prompt Quick Start returns to its first step");
    }
}
