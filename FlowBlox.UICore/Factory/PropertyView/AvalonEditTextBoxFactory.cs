using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Factory.PropertyView.Colorizer;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;

namespace FlowBlox.UICore.Factory.PropertyView
{
    public class AvalonEditTextBoxFactory
    {
        private readonly PropertyInfo _property;
        private readonly object _target;
        private readonly bool _readOnly;
        private readonly FieldRegexResolver _fieldRegexResolver;

        public AvalonEditTextBoxFactory(PropertyInfo property, object target, bool readOnly)
        {
            _property = property ?? throw new ArgumentNullException(nameof(property));
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _readOnly = readOnly;
            _fieldRegexResolver = new FieldRegexResolver();
        }

        private bool _updatingFromModel;
        private bool _updatingToModel;
        private bool _initialized;

        public FrameworkElement Create(FlowBloxTextBoxAttribute textAttr)
        {
            var canEdit = !_readOnly && _property.SetMethod?.IsPublic == true;
            var editor = new TextEditor
            {
                ShowLineNumbers = textAttr.MultiLine,
                IsReadOnly = !canEdit,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = textAttr.MultiLine
                    ? ScrollBarVisibility.Disabled
                    : ScrollBarVisibility.Auto,
                Height = textAttr.MultiLine ? double.NaN : 24
            };
            editor.SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.CodeEditorStyle");

            var highlightingProperty = string.IsNullOrWhiteSpace(textAttr.SyntaxHighlightingMember)
                ? null
                : _target.GetType().GetProperty(textAttr.SyntaxHighlightingMember)
                    ?? throw new InvalidOperationException(
                        $"Syntax highlighting member '{textAttr.SyntaxHighlightingMember}' was not found on '{_target.GetType().FullName}'.");

            var defaultValueProperty = string.IsNullOrWhiteSpace(textAttr.DefaultValueMember)
                ? null
                : _target.GetType().GetProperty(textAttr.DefaultValueMember)
                    ?? throw new InvalidOperationException(
                        $"Default value member '{textAttr.DefaultValueMember}' was not found on '{_target.GetType().FullName}'.");

            void UpdateHighlighting()
            {
                var definition = highlightingProperty == null
                    ? textAttr.SyntaxHighlighting
                    : highlightingProperty.GetValue(_target)?.ToString();
                SetHighlighting(editor, definition);
            }

            if (textAttr.MultiLine)
            {
                ParentScrollViewerMouseWheelForwarder.Register(editor);
            }

            if (_target is INotifyPropertyChanged inpc)
            {
                EventHandler<PropertyChangedEventArgs> propertyChangedEventHandler = (s, e) =>
                {
                    if (!_initialized)
                        return;

                    if (defaultValueProperty != null
                        && (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == defaultValueProperty.Name))
                        _ = editor.Dispatcher.InvokeAsync(editor.TextArea.TextView.Redraw, DispatcherPriority.DataBind);

                    if (highlightingProperty != null
                        && (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == highlightingProperty.Name))
                    {
                        if (editor.Dispatcher.CheckAccess())
                            UpdateHighlighting();
                        else
                            _ = editor.Dispatcher.InvokeAsync(UpdateHighlighting, DispatcherPriority.DataBind);
                    }

                    if (_updatingToModel || (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != _property.Name))
                        return;

                    void UpdateEditor()
                    {
                        if (!_initialized || _updatingToModel)
                            return;

                        _updatingFromModel = true;
                        try
                        {
                            editor.Text = _property.GetValue(_target) as string ?? string.Empty;
                        }
                        finally
                        {
                            _updatingFromModel = false;
                        }
                    }

                    if (editor.Dispatcher.CheckAccess())
                        UpdateEditor();
                    else
                        _ = editor.Dispatcher.InvokeAsync(UpdateEditor, DispatcherPriority.DataBind);
                };

                PropertyChangedEventManager.AddHandler(inpc, propertyChangedEventHandler, string.Empty);
            }

            UpdateHighlighting();

            editor.TextArea.TextView.LineTransformers.Add(new RegexColorizer(
                _fieldRegexResolver.ResolveFieldRegex(), 
                (Brush)new BrushConverter().ConvertFromString("#FF7F00FF"))); // strong violet

            if (defaultValueProperty != null)
            {
                editor.TextArea.TextView.LineTransformers.Add(new DefaultValueColorizer(
                    () => defaultValueProperty.GetValue(_target) as string));
            }

            if (_property.GetValue(_target) is string value)
                editor.Text = value;

            editor.TextChanged += (s, e) =>
            {
                if (_updatingFromModel)
                    return;

                if (canEdit)
                {
                    var current = _property.GetValue(_target) as string;
                    var newText = editor.Text;
                    if (current != newText)
                    {
                        _updatingToModel = true;
                        _property.SetValue(_target, newText);
                        FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                        _updatingToModel = false;

                        // Wait until AvalonEdit closes the current document/undo update.
                        if (defaultValueProperty != null)
                            _ = editor.Dispatcher.InvokeAsync(() => RefreshFallback(editor), DispatcherPriority.DataBind);
                    }
                }
            };

            if (!textAttr.MultiLine)
            {
                editor.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Enter)
                        e.Handled = true;
                };
            }

            _initialized = true;

            return ResizableControlContainer.Create(editor);
        }

        private void RefreshFallback(TextEditor editor)
        {
            var effectiveText = _property.GetValue(_target) as string ?? string.Empty;
            if (editor.Text == effectiveText)
                return;

            _updatingFromModel = true;
            try
            {
                editor.Text = effectiveText;
            }
            finally
            {
                _updatingFromModel = false;
            }
        }

        private void SetHighlighting(TextEditor editor, string resourceOrName)
        {
            // Null and empty names render as plain text without changing the configured value.
            editor.SyntaxHighlighting = null;
            if (string.IsNullOrWhiteSpace(resourceOrName)
                || string.Equals(resourceOrName, "Text", StringComparison.OrdinalIgnoreCase))
                return;

            var builtin = HighlightingManager.Instance.HighlightingDefinitions
                .FirstOrDefault(definition => string.Equals(definition.Name, resourceOrName, StringComparison.OrdinalIgnoreCase));
            if (builtin != null)
            {
                editor.SyntaxHighlighting = builtin;
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => string.Equals(name, resourceOrName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, $"FlowBlox.UICore.Resources.Highlighting.{resourceOrName}.xshd", StringComparison.OrdinalIgnoreCase));
            using var stream = resourceName == null ? null : assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return;

            using var reader = new XmlTextReader(stream);
            var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            editor.SyntaxHighlighting = highlighting;
        }
    }
}
