using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Serializer;
using FlowBlox.AppWindow.Contents;
using FlowBlox.AvaloniaDesktop.Docking;
using FlowBlox.Core.Provider.Project;
using Avalonia.Controls;
using Avalonia;
using System.Runtime.InteropServices;
using Avalonia.Interactivity;
using FlowBlox.AvaloniaDesktop.Dialogs;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsHostedWindowVisible(IntPtr windowHandle);

    private async Task CheckProjectClosingHidesWorkspaceAsync(Action<bool, string> check, string directory)
    {
        var project = FlowBloxProjectManager.Instance.ActiveProject;
        var welcome = _window.FindControl<Image>("Welcome");
        var teardownObserved = false;
        var teardownChecks = new System.Collections.Generic.List<(bool Passed, string Name)>();
        void DuringTeardown(object sender, EventArgs args)
        {
            void Record(bool passed, string name) => teardownChecks.Add((passed, name));
            teardownObserved = true;
            Record(ReferenceEquals(project, FlowBloxProjectManager.Instance.ActiveProject),
                "Closing visibility is checked before the active project has been cleared");
            Record(!Workspace.IsVisible && welcome.IsVisible,
                "Workspace is hidden and welcome is visible during project teardown");
            var visiblePanels = _window.Panels()
                .Where(panel => panel.Handle != IntPtr.Zero && IsHostedWindowVisible(panel.Handle))
                .Select(panel => panel.GetType().Name)
                .ToList();
            Record(visiblePanels.Count == 0,
                $"Hosted WPF windows are hidden before project teardown completes ({string.Join(", ", visiblePanels)})");
            _window.UpdateUI();
            Record(!Workspace.IsVisible,
                "UI refresh during teardown cannot show the closing workspace again");
        }

        project.ExtensionsReloaded += DuringTeardown;
        try
        {
            _window.Activate();
            await Task.Delay(300);
            var samplePoint = Workspace.PointToScreen(new global::Avalonia.Point(10, 10));
            using var beforeClose = CaptureDesktopWindow();
            beforeClose.Save(Path.Combine(directory, "before-project-close.png"));
            _window.itmCloseProject.Execute(null);
            var confirmation = _window.OwnedWindows.OfType<MessageBoxWindow>().Single();
            await Task.Delay(150);
            confirmation.FindControl<Button>("Affirmative").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(500);
            check(FlowBloxProjectManager.Instance.ActiveProject == null,
                "Project closes through its menu and confirmation dialog");
            check(teardownObserved, "Project close exercises the teardown visibility check");
            check(FlowBloxProjectManager.Instance.ActiveProject == null && !Workspace.IsVisible && welcome.IsVisible,
                "Project close leaves only the welcome view visible");
            using var afterClose = CaptureDesktopWindow();
            afterClose.Save(Path.Combine(directory, "after-project-close.png"));
            foreach (var observation in teardownChecks)
                check(observation.Passed, observation.Name);
            var origin = _window.PointToScreen(default);
            var background = afterClose.GetPixel(samplePoint.X - origin.X, samplePoint.Y - origin.Y);
            check(background.R == 0x34 && background.G == 0x34 && background.B == 0x34,
                $"Project close repaints the docking area without mouse input (background={background})");
        }
        finally
        {
            project.ExtensionsReloaded -= DuringTeardown;
        }
    }

    private System.Drawing.Bitmap CaptureDesktopWindow()
    {
        var origin = _window.PointToScreen(default);
        var image = new System.Drawing.Bitmap(
            (int)(_window.ClientSize.Width * _window.RenderScaling),
            (int)(_window.ClientSize.Height * _window.RenderScaling));
        using var graphics = System.Drawing.Graphics.FromImage(image);
        graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, image.Size);
        return image;
    }

    private void CheckWorkspaceObserver(Action<bool, string> check)
    {
        var child = new ToolDock { Id = "Observed", Proportion = 0.5 };
        var root = new RootDock { VisibleDockables = new ObservableCollection<IDockable> { child } };
        var notifications = 0;
        using var observer = new WorkspaceLayoutObserver(root, () => notifications++);
        child.Proportion = 0.4;
        check(notifications > 0, "Layout observer detects splitter proportion changes");
        notifications = 0;
        child.Context = new object();
        check(notifications == 0, "Layout observer ignores runtime-only content changes");
        var replacement = new ToolDock { Id = "Replacement" };
        root.VisibleDockables = new ObservableCollection<IDockable> { replacement };
        notifications = 0;
        child.Proportion = 0.3;
        check(notifications == 0, "Layout observer releases detached model subscriptions");
        replacement.Proportion = 0.6;
        check(notifications > 0, "Layout observer follows replacement collections and models");
        var pinned = new Tool();
        root.LeftPinnedDockables = new ObservableCollection<IDockable> { pinned };
        notifications = 0;
        pinned.Proportion = 0.3;
        check(notifications > 0, "Layout observer follows pinned panel collections");
        var window = new Dock.Model.Mvvm.Core.DockWindow { Layout = new RootDock() };
        root.Windows = new ObservableCollection<IDockWindow> { window };
        notifications = 0;
        window.Width = 650;
        check(notifications > 0, "Layout observer detects floating window size changes");
        observer.Dispose();
        notifications = 0;
        replacement.Proportion = 0.7;
        window.Width = 700;
        check(notifications == 0, "Layout observer unsubscribes when disposed");
    }

    private void CheckProjectCenterDocking(Action<bool, string> check)
    {
        var factory = new WorkspaceFactory(_window.Panels());
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        var project = factory.GetDockable<IDockable>(nameof(ProjectPanel));
        var center = factory.GetDockable<IDock>("Documents");
        var body = factory.GetDockable<IDock>("Body");
        var manager = new DockManager(new DockService());
        check(manager.ValidateDockable(project, body, DragAction.Move, DockOperation.Top, true),
            "Project document can be moved above the workspace");
        check(center.Owner is IDock parent && parent.VisibleDockables.Contains(center),
            "Empty center document dock remains a reachable drop target");
        check(manager.ValidateDockable(project, center, DragAction.Move, DockOperation.Fill, true) &&
              center.VisibleDockables.Contains(project) && project.Owner == center,
            "Project document can be docked back into the center");
        var right = factory.GetDockable<IDock>("Right");
        foreach (var operation in new[] { DockOperation.Left, DockOperation.Right, DockOperation.Top, DockOperation.Bottom })
        {
            check(manager.ValidateDockable(project, right, DragAction.Move, operation, true),
                $"Project panel can dock {operation} beside a tool panel");
            check(manager.ValidateDockable(project, center, DragAction.Move, DockOperation.Fill, true),
                $"Project panel returns to center after docking {operation}");
        }
        check(manager.ValidateDockable(project, right, DragAction.Move, DockOperation.Window, false),
            "Project panel supports floating window docking");

        manager.ValidateDockable(project, body, DragAction.Move, DockOperation.Top, true);
        center.IsCollapsable = true;
        factory.CollapseDock(center);
        var serializer = new DockSerializer();
        var restored = serializer.Deserialize<IRootDock>(serializer.Serialize<IRootDock>(layout));
        factory.DetachContent(layout);
        factory.InitLayout(restored);
        var restoredCenter = factory.GetDockable<IDock>("Documents");
        check(restoredCenter?.Owner is IDock centerOwner && centerOwner.VisibleDockables.Contains(restoredCenter),
            "Loading an older layout repairs a missing center drop target");
        var restoredProject = factory.GetDockable<IDockable>(nameof(ProjectPanel));
        check(manager.ValidateDockable(restoredProject, restoredCenter, DragAction.Move, DockOperation.Fill, true),
            "Project returns to the repaired center after layout reload");
        factory.DetachContent(restored);

        var oldLayout = factory.CreateLayout();
        var oldCenter = (IDock)FindSavedDock(oldLayout, "Documents");
        var oldProject = new PanelDocument { Id = nameof(ProjectPanel), CanFloat = false };
        oldCenter.VisibleDockables[0] = oldProject;
        oldCenter.ActiveDockable = oldProject;
        factory.InitLayout(oldLayout);
        check(factory.GetDockable<IDockable>(nameof(ProjectPanel)) is ITool { CanFloat: true } upgraded &&
              oldCenter.ActiveDockable == upgraded && upgraded.Owner == oldCenter,
            "Saved project documents upgrade to unrestricted docking without changing their dock");
        factory.DetachContent(oldLayout);
        factory.DetachContent(layout);
    }

    private async Task CheckAutomaticLayoutSaveAsync(string directory, Action<bool, string> check)
    {
        async Task WaitFor(Func<bool> condition, string name)
        {
            for (var attempt = 0; attempt < 30 && !condition(); attempt++) await Task.Delay(100);
            check(condition(), name);
        }

        var left = _window.WorkspaceFactory.GetDockable<IDockable>("Left");
        var previousProportion = left.Proportion;
        left.Proportion = previousProportion * 0.75;
        await WaitFor(() => FindSavedDock(WorkspacePersistence.Load(), "Left") is { } saved &&
            Math.Abs(saved.Proportion - left.Proportion) < 0.0000001 &&
            Math.Abs(saved.Proportion - previousProportion) > 0.000001,
            "Splitter changes persist automatically without explicit SaveLayout");

        _window.ActivatePanel(TestPanel);
        await WaitFor(() => (FindSavedDock(WorkspacePersistence.Load(), "Bottom") as IDock)?.ActiveDockable?.Id == nameof(TestView),
            "Tab selection persists automatically");

        var field = _window.WorkspaceFactory.GetDockable<IDockable>(nameof(FieldView));
        _window.WorkspaceFactory.HideDockable(field);
        await WaitFor(() => WorkspacePersistence.Load()?.HiddenDockables?.Any(x => x.Id == nameof(FieldView)) == true,
            "Hidden panels persist automatically");
        _window.ActivatePanel(FieldPanel);
        await WaitFor(() => WorkspacePersistence.Load()?.HiddenDockables?.All(x => x.Id != nameof(FieldView)) == true,
            "Restored panels persist automatically");

        var workspaceRoot = (IRootDock)Workspace.Layout;
        var project = _window.WorkspaceFactory.GetDockable<IDockable>(nameof(ProjectPanel));
        var center = (IDock)FindSavedDock(workspaceRoot, "Documents");
        _window.WorkspaceFactory.FloatDockable(project);
        await WaitFor(() => workspaceRoot.Windows?.Any(window =>
            FindSavedDock(window.Layout, nameof(ProjectPanel)) != null) == true &&
            ProjectPanelView.Handle != IntPtr.Zero && ProjectPanelView.WpfContent.IsLoaded,
            "Project WPF view remains loaded in a real floating window");
        var floatingWindow = workspaceRoot.Windows.First(window => FindSavedDock(window.Layout, nameof(ProjectPanel)) != null);
        floatingWindow.Width += 30;
        await WaitFor(() => WorkspacePersistence.Load()?.Windows?.Any(window =>
            window.Id == floatingWindow.Id && Math.Abs(window.Width - floatingWindow.Width) < 0.01) == true,
            "Floating window dimensions persist automatically");
        var manager = new DockManager(new DockService());
        check(manager.ValidateDockable(project, center, DragAction.Move, DockOperation.Fill, true),
            "Floating project panel docks back into the center");
        await WaitFor(() => project.Owner == center && ProjectPanelView.Handle != IntPtr.Zero &&
            ProjectPanelView.WpfContent.IsLoaded &&
            (workspaceRoot.Windows == null || workspaceRoot.Windows.Count == 0),
            "WPF view reattaches after docking and the empty floating window closes");

        var previousWidth = _window.Width;
        _window.Width = previousWidth + 20;
        await WaitFor(() => WorkspacePersistence.LoadWindow()?.Width == _window.Width,
            "Main window size persists automatically");
        _window.Width = previousWidth;
        await WaitFor(() => WorkspacePersistence.LoadWindow()?.Width == previousWidth,
            "Main window size restoration persists automatically");
        await Task.Delay(700);
        var layoutTimestamp = File.GetLastWriteTimeUtc(Path.Combine(directory, "layout.json"));
        var windowTimestamp = File.GetLastWriteTimeUtc(Path.Combine(directory, "window.json"));
        await Task.Delay(2500);
        check(!_window.IsWorkspaceSavePending && layoutTimestamp == File.GetLastWriteTimeUtc(Path.Combine(directory, "layout.json")) &&
              windowTimestamp == File.GetLastWriteTimeUtc(Path.Combine(directory, "window.json")),
            "Idle workspace does not write layout or window files");
    }

    private static IDockable FindSavedDock(IDockable item, string id)
    {
        if (item?.Id == id) return item;
        if (item is IDock { VisibleDockables: { } children })
            foreach (var child in children)
                if (FindSavedDock(child, id) is { } found) return found;
        return null;
    }
}
