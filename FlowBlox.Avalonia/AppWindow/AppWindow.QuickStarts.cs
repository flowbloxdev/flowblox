using System.Linq;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.AvaloniaDesktop.ViewModels;
using FlowBlox.Core;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.PopUp.Provider;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    private readonly MenuEntry _quickStartsMenu = new();
    private IComponentPopupProvider[] _quickStartProviders = [];

    private void InitializeQuickStartsMenu()
    {
        _quickStartsMenu.Text = FlowBloxResourceUtil.GetLocalizedString("QuickStarts_MenuTitle", typeof(FlowBloxTexts));
        mnItmHelp.DropDownItems.Insert(mnItmHelp.DropDownItems.IndexOf(toolStripSeparator15), _quickStartsMenu);
        UpdateQuickStartsMenu();
    }

    private void UpdateQuickStartsMenu()
    {
        var menu = _quickStartsMenu;
        var providers = FlowBloxServiceLocator.Instance.GetServices<IComponentPopupProvider>()
            .Where(provider => provider.IsQuickStart)
            .GroupBy(provider => provider.OptionKey)
            .Select(group => group.First())
            .OrderBy(provider => provider.QuickStartTitle)
            .ToArray();
        if (_quickStartProviders.SequenceEqual(providers))
            return;

        _quickStartProviders = providers;
        menu.DropDownItems.Clear();
        foreach (var provider in providers)
        {
            var entry = new MenuEntry
            {
                Text = provider.QuickStartTitle,
                Tag = provider.TargetType
            };
            entry.Click += (_, _) => ShowQuickStart(provider);
            menu.DropDownItems.Add(entry);
        }

    }

    private void ShowQuickStart(IComponentPopupProvider provider)
    {
        var window = provider.CreateQuickStartWindow();
        if (window != null)
            WpfDialogHost.ShowDialog(window, this);
    }
}
