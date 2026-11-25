using FlowBlox.Core;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FlowBlox.UICore.Views.ProjectExecution;

/// <summary>
/// Edits a serialized input list using the standard field editor for each row.
/// </summary>
internal sealed class FieldListEditor : StackPanel
{
    private readonly FieldElement _field;
    private readonly Window _window;
    private readonly Func<FieldElement, FrameworkElement, FrameworkElement> _decorate;
    private readonly StackPanel _rows = new();
    private readonly List<FieldElement> _entries = [];
    private bool _publishing;
    private bool _refreshPending;

    public FieldListEditor(
        FieldElement field,
        Window window,
        Func<FieldElement, FrameworkElement, FrameworkElement> decorate)
    {
        _field = field;
        _window = window;
        _decorate = decorate;
        Children.Add(_rows);
        var addContent = new PackIconMaterial
        {
            Kind = PackIconMaterialKind.Plus,
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        var addLabel = FlowBloxResourceUtil.GetLocalizedString("FieldElement_AddListItem", typeof(FlowBloxTexts));
        var add = new Button
        {
            Content = addContent,
            ToolTip = addLabel
        };
        add.SetResourceReference(StyleProperty, "Execution.ListAddButton");
        System.Windows.Automation.AutomationProperties.SetName(add, addLabel);
        add.Click += (_, _) =>
        {
            AddEntry(string.Empty);
            Publish();
            FocusEntry(_entries.Count - 1);
        };
        Children.Add(add);

        PropertyChangedEventManager.AddHandler(field, OnFieldChanged, string.Empty);
        ReadValue();
    }

    private void OnFieldChanged(object sender, PropertyChangedEventArgs args)
    {
        if (_publishing || (!string.IsNullOrEmpty(args.PropertyName)
            && args.PropertyName != nameof(FieldElement.StringValue)
            && args.PropertyName != nameof(FieldElement.DefaultValue)
            && args.PropertyName != nameof(FieldElement.ListSeparator)))
            return;

        // Rebuilding while AvalonEdit is updating its document would break its undo transaction.
        if (_refreshPending)
            return;

        _refreshPending = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            _refreshPending = false;
            ReadValue();
        }, DispatcherPriority.DataBind);
    }

    private void ReadValue()
    {
        _entries.Clear();
        _rows.Children.Clear();
        foreach (var value in FieldListPresentation.Split(_field.StringValue, _field.ListSeparator))
            AddEntry(value);
    }

    private void AddEntry(string value)
    {
        var entry = new FieldElement(FieldTypes.Text)
        {
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            DisplayMode = FieldDisplayMode.Multiline,
            SyntaxHighlighting = _field.SyntaxHighlighting,
            LoVType = _field.LoVType,
            ListOfValues = _field.ListOfValues,
            StringValue = value
        };
        _entries.Add(entry);
        var resolver = new PropertyControlResolver(_window);
        var model = resolver.Resolve(
            typeof(FieldElement).GetProperty(nameof(FieldElement.StringValue)),
            entry,
            false,
            null,
            new PropertyControlBindingContext(entry));
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var remove = new Button
        {
            Content = new PackIconMaterial
            {
                Kind = PackIconMaterialKind.Minus,
                Width = 14,
                Height = 14
            },
            ToolTip = FlowBloxResourceUtil.GetLocalizedString("FieldElement_RemoveListItem", typeof(FlowBloxTexts))
        };
        remove.SetResourceReference(StyleProperty, "Execution.ListRemoveButton");
        System.Windows.Automation.AutomationProperties.SetName(remove, (string)remove.ToolTip);
        DockPanel.SetDock(remove, Dock.Right);
        remove.Click += (_, _) =>
        {
            _entries.Remove(entry);
            _rows.Children.Remove(row);
            if (_entries.Count == 0)
                AddEntry(string.Empty);

            Publish();
        };
        row.Children.Add(remove);
        row.Children.Add(_decorate(entry, model.Control));
        _rows.Children.Add(row);
        entry.PropertyChanged += OnEntryChanged;
    }

    private void OnEntryChanged(object sender, PropertyChangedEventArgs args)
    {
        if (_publishing || args.PropertyName != nameof(FieldElement.StringValue))
            return;

        var entry = (FieldElement)sender;
        var parts = FieldListPresentation.Split(entry.StringValue, _field.ListSeparator);
        if (parts.Length == 1)
        {
            Publish();
            return;
        }

        _ = Dispatcher.InvokeAsync(() => Distribute(entry), DispatcherPriority.DataBind);
    }

    private void Distribute(FieldElement entry)
    {
        var index = _entries.IndexOf(entry);
        if (index < 0)
            return;

        var values = _entries.Select(item => item.StringValue ?? string.Empty).ToList();
        FieldListPresentation.Apply(values, index, entry.StringValue, _field.ListSeparator);
        _publishing = true;
        try
        {
            for (var row = index; row < values.Count; row++)
            {
                if (row < _entries.Count)
                {
                    _entries[row].StringValue = values[row];
                    FlowBloxComponentHelper.RaisePropertyChanged(_entries[row], nameof(FieldElement.StringValue));
                }
                else
                {
                    AddEntry(values[row]);
                }
            }
        }
        finally
        {
            _publishing = false;
        }

        Publish();
    }

    private void Publish()
    {
        var value = string.Join(
            FieldListPresentation.ResolveSeparator(_field.ListSeparator),
            _entries.Select(entry => entry.StringValue ?? string.Empty));
        _publishing = true;
        try
        {
            _field.StringValue = value;
            FlowBloxComponentHelper.RaisePropertyChanged(_field, nameof(FieldElement.StringValue));
        }
        finally
        {
            _publishing = false;
        }

        if (_field.StringValue != value)
            OnFieldChanged(_field, new PropertyChangedEventArgs(nameof(FieldElement.StringValue)));
    }

    private void FocusEntry(int index)
    {
        _ = Dispatcher.InvokeAsync(() => _rows.Children[index].MoveFocus(
            new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First)));
    }
}
