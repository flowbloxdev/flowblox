using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using FlowBlox.AppWindow.Contents;

namespace FlowBlox.AvaloniaDesktop.Docking;

public sealed class WorkspaceFactory : Factory
{
    private readonly Dictionary<string, WpfPanel> _panels;
    private bool _hideProblemsOnInit;
    public WorkspaceFactory(IEnumerable<WpfPanel> panels) => _panels = panels.ToDictionary(x => x.GetType().Name);
    public override IRootDock CreateRootDock() => new WorkspaceRoot();

    private PanelTool Tool(string id) => new() { Id = id, Title = _panels[id].Text, CanClose = true, CanPin = true };

    public override IRootDock CreateLayout()
    {
        _hideProblemsOnInit = true;
        // This is a single-project workspace: use the same unrestricted docking model
        // as the other panels, including docking into tool groups and floating windows.
        var project = Tool(nameof(ProjectPanel));
        project.CanClose = false;
        var library = Tool(nameof(ComponentLibraryView));
        var field = Tool(nameof(FieldView));
        var assistant = Tool(nameof(AIAssistantView));
        var objects = Tool(nameof(ManagedObjectsView));
        var tests = Tool(nameof(TestView));
        var runtime = Tool(nameof(RuntimeView));
        var problems = Tool(nameof(ProblemsView));
        var upper = new ProportionalDock
        {
            Id = "Upper", Orientation = Orientation.Horizontal, Proportion = 0.77,
            VisibleDockables = CreateList<IDockable>(
                new ToolDock { Id = "Left", Alignment = Alignment.Left, Proportion = 0.23, ActiveDockable = library, VisibleDockables = CreateList<IDockable>(library) },
                new ProportionalDockSplitter(),
                new DocumentDock { Id = "Documents", Proportion = 0.54, ActiveDockable = project, VisibleDockables = CreateList<IDockable>(project), CanCreateDocument = false, IsCollapsable = false, CanDrag = false },
                new ProportionalDockSplitter(),
                new ToolDock { Id = "Right", Alignment = Alignment.Right, Proportion = 0.23, ActiveDockable = field, VisibleDockables = CreateList<IDockable>(field, assistant) })
        };
        var body = new ProportionalDock
        {
            Id = "Body", Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(upper, new ProportionalDockSplitter(),
                new ToolDock { Id = "Bottom", Alignment = Alignment.Bottom, Proportion = 0.23, ActiveDockable = objects, VisibleDockables = CreateList<IDockable>(objects, tests, runtime, problems) })
        };
        var root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(body);
        root.ActiveDockable = root.DefaultDockable = body;
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        UpgradeProjectDockable(layout);
        EnsureCenterDocumentDock(layout);
        ContextLocator = _panels.ToDictionary(x => x.Key, x => (Func<object>)(() => x.Key));
        DockableLocator = new Dictionary<string, Func<IDockable>>();
        foreach (var dockable in Walk(layout))
        {
            if (!string.IsNullOrEmpty(dockable.Id)) DockableLocator[dockable.Id] = () => dockable;
            AttachContent(dockable);
        }
        HostWindowLocator = new Dictionary<string, Func<IHostWindow>> { [nameof(IDockWindow)] = () => new HostWindow() };
        base.InitLayout(layout);
        foreach (var dockable in Walk(layout).Where(x => x.Factory != this).ToArray())
            InitDockable(dockable, dockable.Owner);
        if (_hideProblemsOnInit)
        {
            _hideProblemsOnInit = false;
            HideDockable(GetDockable<IDockable>(nameof(ProblemsView)));
        }
    }

    private void UpgradeProjectDockable(IDockable layout)
    {
        var dockables = Walk(layout).ToArray();
        var legacy = dockables.OfType<PanelDocument>().FirstOrDefault(x => x.Id == nameof(ProjectPanel));
        if (legacy == null) return;
        var project = Tool(nameof(ProjectPanel));
        project.CanClose = false;
        project.Proportion = legacy.Proportion;
        project.CollapsedProportion = legacy.CollapsedProportion;
        project.Dock = legacy.Dock;
        project.Owner = legacy.Owner;
        project.OriginalOwner = legacy.OriginalOwner;
        void Replace(IList<IDockable> items)
        {
            if (items == null) return;
            for (var i = 0; i < items.Count; i++)
                if (ReferenceEquals(items[i], legacy)) items[i] = project;
        }
        foreach (var dockable in dockables)
        {
            if (dockable is IDock dock)
            {
                Replace(dock.VisibleDockables);
                if (ReferenceEquals(dock.ActiveDockable, legacy)) dock.ActiveDockable = project;
                if (ReferenceEquals(dock.DefaultDockable, legacy)) dock.DefaultDockable = project;
                if (ReferenceEquals(dock.FocusedDockable, legacy)) dock.FocusedDockable = project;
            }
            if (dockable is IRootDock root)
            {
                Replace(root.HiddenDockables);
                Replace(root.LeftPinnedDockables); Replace(root.RightPinnedDockables);
                Replace(root.TopPinnedDockables); Replace(root.BottomPinnedDockables);
            }
        }
    }

    private void EnsureCenterDocumentDock(IDockable layout)
    {
        var dockables = Walk(layout).ToArray();
        if (dockables.OfType<IDocumentDock>().FirstOrDefault(x => x.Id == "Documents") is { } center)
        {
            // Keep the host stationary; its Project document remains draggable.
            center.IsCollapsable = false;
            center.CanDrag = false;
            return;
        }

        // Older saved layouts removed this empty host when Project was moved elsewhere.
        if (dockables.OfType<IProportionalDock>().FirstOrDefault(x => x.Id == "Upper") is not { } upper ||
            upper.VisibleDockables == null) return;
        var position = upper.VisibleDockables.ToList().FindIndex(x => x.Id == "Right");
        if (position < 0) position = upper.VisibleDockables.Count;
        upper.VisibleDockables.Insert(position, new DocumentDock
        {
            Id = "Documents", Proportion = 0.54, CanCreateDocument = false,
            IsCollapsable = false, CanDrag = false, VisibleDockables = CreateList<IDockable>()
        });
        upper.VisibleDockables.Insert(position + 1, new ProportionalDockSplitter());
    }

    public override void InitDockable(IDockable dockable, IDockable owner)
    {
        if (!string.IsNullOrEmpty(dockable.Id)) DockableLocator[dockable.Id] = () => dockable;
        AttachContent(dockable);
        base.InitDockable(dockable, owner);
    }

    private void AttachContent(IDockable dockable)
    {
        if (_panels.TryGetValue(dockable.Id ?? string.Empty, out var panel))
        {
            if (dockable is PanelDocument document) document.Content = panel;
            if (dockable is PanelTool tool) tool.Content = panel;
        }
    }

    private static IEnumerable<IDockable> Walk(IDockable item)
    {
        var pending = new Stack<IDockable>();
        var visited = new HashSet<IDockable>();
        pending.Push(item);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            yield return current;
            void Add(IEnumerable<IDockable> items) { if (items != null) foreach (var child in items) pending.Push(child); }
            if (current is IDock dock) Add(dock.VisibleDockables);
            if (current is IRootDock root)
            {
                Add(root.HiddenDockables); Add(root.LeftPinnedDockables); Add(root.RightPinnedDockables);
                Add(root.TopPinnedDockables); Add(root.BottomPinnedDockables);
                if (root.Windows != null) foreach (var window in root.Windows) if (window.Layout != null) pending.Push(window.Layout);
            }
        }
    }

    public override void CloseDockable(IDockable dockable) => HideDockable(dockable);

    public override void HideDockable(IDockable dockable)
    {
        if (FindRoot(dockable, _ => true) is WorkspaceRoot root && dockable.Owner != null && dockable.Owner != root)
            root.HiddenOwners[dockable.Id] = dockable.Owner;
        base.HideDockable(dockable);
    }

    public override void RestoreDockable(IDockable dockable)
    {
        var root = FindRoot(dockable, _ => true) as WorkspaceRoot;
        if (dockable.OriginalOwner == null && root?.HiddenOwners.TryGetValue(dockable.Id, out var owner) == true)
            dockable.OriginalOwner = owner;
        base.RestoreDockable(dockable);
        if (dockable.Owner != null) InitDockable(dockable, dockable.Owner);
        root?.HiddenOwners.Remove(dockable.Id);
    }

    public void DetachContent(IDockable layout)
    {
        if (layout == null) return;
        foreach (var item in Walk(layout))
        {
            if (item is PanelDocument document) document.Content = null;
            if (item is PanelTool tool) tool.Content = null;
        }
    }

    public void Activate(string id)
    {
        var dockable = GetDockable<IDockable>(id);
        if (dockable == null) return;
        if (dockable.Owner is IRootDock root && root.HiddenDockables?.Contains(dockable) == true)
            RestoreDockable(dockable);
        SetActiveDockable(dockable);
    }

    public void Focus(string id)
    {
        var dockable = GetDockable<IDockable>(id);
        if (dockable?.Owner is not IDock owner) return;
        if (owner.ActiveDockable != dockable) SetActiveDockable(dockable);
        SetFocusedDockable(owner, dockable);
    }
}
