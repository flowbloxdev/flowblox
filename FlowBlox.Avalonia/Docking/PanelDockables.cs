using System.Runtime.Serialization;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using System.Collections.Generic;
using Dock.Model.Core;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using System.Linq;

namespace FlowBlox.AvaloniaDesktop.Docking;

public sealed class WorkspaceRoot : RootDock
{
    [DataMember]
    public Dictionary<string, IDockable> HiddenOwners { get; set; } = new();
}

// Persist layout models, then reattach the live controls through the factory.
public sealed class PanelDocument : Document, IDocumentContent, IDataTemplate
{
    private object _content;
    private readonly PanelContent _view = new();
    [IgnoreDataMember, System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public object Content { get => _content; set { SetProperty(ref _content, value); _view.SetContent(value); } }
    public bool Match(object data) => ReferenceEquals(data, this);
    public Control Build(object data) => _view.Build();
}

public sealed class PanelTool : Tool, IToolContent, IDataTemplate
{
    private object _content;
    private readonly PanelContent _view = new();
    [IgnoreDataMember, System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public object Content { get => _content; set { SetProperty(ref _content, value); _view.SetContent(value); } }
    public bool Match(object data) => ReferenceEquals(data, this);
    public Control Build(object data) => _view.Build();
}

internal sealed class PanelContent
{
    private object _content;
    private ContentControl _host;
    public void SetContent(object content)
    {
        _content = content;
        if (_host != null) _host.Content = content;
    }

    public Control Build()
    {
        // Dock can retain presenters for inactive tabs. A native view has one parent.
        if (_host != null)
        {
            _host.Content = null;
            foreach (var presenter in _host.GetVisualDescendants().OfType<ContentPresenter>().ToArray()) presenter.UpdateChild();
        }
        _host = new ContentControl
        {
            Content = _content,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        return _host;
    }
}
