using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FlowBlox.AppWindow.Contents;
using FlowBlox.AvaloniaDesktop.ViewModels;
using FlowBlox.Core.Logging;
using Dock.Model.Core;
using FlowBlox.AvaloniaDesktop.Docking;
using Avalonia.Input;
using FlowBlox.AvaloniaDesktop.Interop.Windows;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    public ObservableCollection<MenuEntry> Menus { get; } = new();
    private WorkspaceFactory _factory;
    internal WorkspaceFactory WorkspaceFactory => _factory;
    private bool _layoutReady;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        MainMenu = this.FindControl<Menu>(nameof(MainMenu));
        NotificationAction = this.FindControl<Button>(nameof(NotificationAction));
        NotificationMessage = this.FindControl<TextBlock>(nameof(NotificationMessage));
        NotificationBar = this.FindControl<Border>(nameof(NotificationBar));
        Welcome = this.FindControl<Image>(nameof(Welcome));
        Workspace = this.FindControl<Dock.Avalonia.Controls.DockControl>(nameof(Workspace));
        LoadingOverlay = this.FindControl<Border>(nameof(LoadingOverlay));
        LoadingText = this.FindControl<TextBlock>(nameof(LoadingText));
        InitializeMenu();
        MainMenu.ItemsSource = Menus.Select(BuildMenuItem).ToArray();
        NotificationAction.Click += lblNotificationAction_Click;
        Opened += AppWindow_Load;
        Closing += AppWindow_FormClosing;
        ComponentDispatcher.ThreadFilterMessage += FilterHostedMessage;
        Closed += (_, _) =>
        {
            ComponentDispatcher.ThreadFilterMessage -= FilterHostedMessage;
            DisposeWorkspacePersistence();
        };
        RestoreWindowBounds();
        InitializeWorkspacePersistence();
    }

    private static Bitmap LoadAsset(string name) => new(AssetLoader.Open(new Uri($"avares://{typeof(AppWindow).Assembly.GetName().Name}/Assets/{name}")));

    private Control BuildMenuItem(MenuEntry entry)
    {
        if (entry.IsSeparator) return new Separator();
        var item = new MenuItem { Header = entry.Text?.Replace('&', '_'), Command = entry, HotKey = entry.Gesture, InputGesture = entry.Gesture };
        item.Bind(IsEnabledProperty, new Binding(nameof(MenuEntry.Enabled)) { Source = entry });
        item.Bind(MenuItem.IsCheckedProperty, new Binding(nameof(MenuEntry.IsChecked)) { Source = entry });
        if (entry.Icon != null) item.Icon = new Image { Source = entry.Icon, Width = 16, Height = 16 };
        void RefreshChildren() => item.ItemsSource = entry.DropDownItems.Select(BuildMenuItem).ToArray();
        RefreshChildren();
        entry.DropDownItems.CollectionChanged += (_, _) => RefreshChildren();
        return item;
    }

    private IEnumerable<MenuEntry> AllMenuEntries()
    {
        IEnumerable<MenuEntry> Walk(IEnumerable<MenuEntry> source) => source.SelectMany(x => new[] { x }.Concat(Walk(x.DropDownItems)));
        return Walk(Menus);
    }

    private void FilterHostedMessage(ref MSG message, ref bool handled)
    {
        if (handled || message.message != 0x100 || !IsActive) return;
        handled = ProcessNativeKey(message.wParam.ToInt32());
    }

    internal bool ProcessNativeKey(int key)
    {
        var inputKey = HostedKeyboard.KeyFromVirtualKey(key);
        var modifiers = HostedKeyboard.Modifiers;
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox)
        {
            if (modifiers == KeyModifiers.Control && inputKey is Key.C or Key.V or Key.X or Key.Z or Key.Y or Key.A
                || inputKey is Key.Delete or Key.Back) return false;
        }
        return ProcessHostedShortcut(inputKey, modifiers);
    }

    private void RuntimeStateChanged(object sender, FlowBlox.UICore.Events.RuntimeStateChangedEventArgs e) => UpdateUI();

    private void InitializeDockPanel(bool exceptProjectPanel = false, bool exceptAiAssistantView = false)
    {
        SaveLayout();
        _layoutReady = false;
        _factory?.DetachContent(Workspace.Layout);
        Workspace.Layout = null;
        this.UpdateLayout();
        DisposePanels(exceptAiAssistantView);
        _dockContentProjectPanel = new ProjectPanel();
        _componentLibraryPanel = new ComponentLibraryView();
        _fieldViewPanel = new FieldView();
        _managedObjectsViewPanel = new ManagedObjectsView();
        _testViewPanel = new TestView();
        _runtimeViewPanel = new Contents.RuntimeView();
        _problemsViewPanel = new Contents.ProblemsView();
        if (!exceptAiAssistantView || _aiAssistantViewPanel == null) _aiAssistantViewPanel = new AIAssistantView();
        _aiAssistantViewPanel.ConfigureBeforeAutomaticLayoutAdjustment(() => _dockContentProjectPanel?.SyncNodeSizesToModel());
        var panels = Panels().ToArray();
        _factory = new WorkspaceFactory(panels);
        var layout = WorkspacePersistence.Load() ?? _factory.CreateLayout();
        _factory.InitLayout(layout);
        Workspace.Factory = _factory;
        Workspace.Layout = layout;
        itmDockablePanels.DropDownItems.Clear();
        foreach (var panel in panels.Where(x => x != _dockContentProjectPanel))
        {
            var entry = new MenuEntry { Text = panel.Text, Tag = panel, Icon = LoadPanelIcon(panel) };
            entry.Click += (_, _) => ActivatePanel(panel);
            itmDockablePanels.DropDownItems.Add(entry);
        }
        _layoutReady = true;
        RequestLayoutSave();
    }

    internal IEnumerable<WpfPanel> Panels() => new WpfPanel[] { _dockContentProjectPanel, _componentLibraryPanel, _fieldViewPanel, _aiAssistantViewPanel, _managedObjectsViewPanel, _testViewPanel, _runtimeViewPanel, _problemsViewPanel }.Where(x => x != null);
    private void DisposePanels(bool preserveAssistant = false)
    {
        foreach (var panel in Panels().Where(x => !preserveAssistant || x != _aiAssistantViewPanel)) panel.Dispose();
    }
    public void ActivatePanel(WpfPanel panel) => _factory?.Activate(panel.GetType().Name);
    internal void NotifyHostedPanelFocused(WpfPanel panel) => _factory?.Focus(panel.GetType().Name);
    internal void SaveLayout()
    {
        _pendingLayoutSave?.Dispose();
        _pendingLayoutSave = null;
        if (_layoutReady && Workspace.Layout is Dock.Model.Controls.IRootDock root) WorkspacePersistence.Save(root);
        SaveWindowBounds();
    }
    private void ResetLayout()
    {
        if (_factory == null) return;
        _layoutReady = false;
        _factory.DetachContent(Workspace.Layout);
        Workspace.Layout = null;
        this.UpdateLayout();
        var layout = _factory.CreateLayout();
        _factory.InitLayout(layout);
        Workspace.Layout = layout;
        _layoutReady = true;
        SaveLayout();
    }

    private void RestoreWindowBounds()
    {
        var bounds = WorkspacePersistence.LoadWindow();
        if (bounds == null) return;
        Width = Math.Max(800, bounds.Width); Height = Math.Max(600, bounds.Height);
        if (bounds.Maximized) WindowState = WindowState.Maximized;
    }
    private void SaveWindowBounds() => WorkspacePersistence.SaveWindow(new(Width, Height, WindowState == WindowState.Maximized));

    private static Bitmap LoadPanelIcon(WpfPanel panel)
    {
        var key = panel switch
        {
            ProjectPanel => "ProjectPanel_16",
            ComponentLibraryView => "ComponentLibraryPanel_16",
            FieldView => "FieldView_16",
            ManagedObjectsView => "ManagedObjectsView_16",
            AIAssistantView => "AIAssistantView_16",
            Contents.ProblemsView => "ProblemsView_16",
            Contents.RuntimeView => "RuntimeView_16",
            _ => "DockContent_16"
        };
        return LoadAsset("Panels/" + key + ".png");
    }
}
