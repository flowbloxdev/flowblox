using System.Collections.Generic;

namespace FlowBlox.AvaloniaDesktop.ViewModels;

public sealed class ProjectShortcutSection
{
    public string Title { get; set; }
    public IReadOnlyList<ProjectShortcutItem> Items { get; set; }
}
