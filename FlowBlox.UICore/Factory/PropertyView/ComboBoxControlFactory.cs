using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.PropertyView.Resolver;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Utilities;

namespace FlowBlox.UICore.Factory.PropertyView
{
    /// <summary>
    /// Attribute-driven selection and scalar suggestion editors for property views and grid cells.
    /// </summary>
    public sealed class ComboBoxControlFactory
    {
        private readonly Window _window;
        private readonly PropertyInfo _property;
        private readonly object _target;
        private readonly object _parent;
        private readonly bool _readOnly;
        private readonly FlowBloxUIAttribute _ui;
        private readonly FlowBloxComboBoxAttribute _combo;
        private readonly FlowBloxTextBoxAttribute _text;

        public ComboBoxControlFactory(
            Window window,
            PropertyInfo property,
            object target,
            object parent,
            bool readOnly)
        {
            _window = window;
            _property = property;
            _target = target;
            _parent = parent;
            _readOnly = readOnly;
            _ui = property.GetCustomAttribute<FlowBloxUIAttribute>();
            _combo = property.GetCustomAttribute<FlowBloxComboBoxAttribute>() ?? new FlowBloxComboBoxAttribute();
            _text = property.GetCustomAttribute<FlowBloxTextBoxAttribute>();
        }

        public FrameworkElement Create()
        {
            var host = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            var mode = ResolveMode();
            var password = IsPassword();
            var required = SelectionItemsSourceResolver.IsRequired(_property, _target);
            var dynamicallyReadOnly = FlowBlockUIAttributeHelper.IsDynamicallyReadOnly(_target, _ui);
            INotifyCollectionChanged observedItems = null;

            void ItemsChanged(object sender, NotifyCollectionChangedEventArgs args) => Refresh();

            void Refresh()
            {
                if (observedItems != null)
                    CollectionChangedEventManager.RemoveHandler(observedItems, ItemsChanged);
                mode = ResolveMode();
                password = IsPassword();
                required = SelectionItemsSourceResolver.IsRequired(_property, _target);
                dynamicallyReadOnly = FlowBlockUIAttributeHelper.IsDynamicallyReadOnly(_target, _ui);
                var source = SelectionMethodResolver.ResolveSelectionFilterMethodFromTargetOrParent(
                    _target, _parent, _ui?.SelectionFilterMethod);
                var rawItems = source?.Method?.Invoke(source.InvocationTarget, null) as IEnumerable;
                observedItems = rawItems as INotifyCollectionChanged;
                if (host.IsLoaded && observedItems != null)
                    CollectionChangedEventManager.AddHandler(observedItems, ItemsChanged);
                var valueType = Nullable.GetUnderlyingType(_property.PropertyType) ?? _property.PropertyType;
                var items = valueType.IsEnum && string.IsNullOrWhiteSpace(_ui?.SelectionFilterMethod)
                    ? EnumerationDropDown.CreateItems(valueType, !required && Nullable.GetUnderlyingType(_property.PropertyType) != null)
                    : CreateItems(rawItems, required);
                host.Content = BuildEditor(mode, password, items);
            }

            void ModelChanged(object sender, PropertyChangedEventArgs args)
            {
                if (mode != ResolveMode() || password != IsPassword()
                    || required != SelectionItemsSourceResolver.IsRequired(_property, _target)
                    || dynamicallyReadOnly != FlowBlockUIAttributeHelper.IsDynamicallyReadOnly(_target, _ui)
                    || args.PropertyName == _ui?.SelectionFilterDependency)
                    Refresh();
            }

            void Subscribe(object model, bool add)
            {
                if (model is not INotifyPropertyChanged observable)
                    return;
                PropertyChangedEventManager.RemoveHandler(observable, ModelChanged, string.Empty);
                if (add)
                    PropertyChangedEventManager.AddHandler(observable, ModelChanged, string.Empty);
            }

            host.Loaded += (_, _) =>
            {
                Subscribe(_target, true);
                if (!ReferenceEquals(_target, _parent))
                    Subscribe(_parent, true);
                Refresh();
            };
            host.Unloaded += (_, _) =>
            {
                Subscribe(_target, false);
                Subscribe(_parent, false);
                if (observedItems != null)
                    CollectionChangedEventManager.RemoveHandler(observedItems, ItemsChanged);
            };
            Refresh();
            return host;
        }

        private DropDownMode ResolveMode()
        {
            if (!PropertyTypeHelper.IsSimpleType(_property.PropertyType))
                return DropDownMode.SelectionOnly;
            return ResolveMethodValue(_combo.DropDownModeMethod, _combo.DropDownMode);
        }

        private bool IsPassword() => ResolveMethodValue(_text?.IsPasswordMethod, _text?.IsPassword ?? false);

        private T ResolveMethodValue<T>(string methodName, T fallback)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                return fallback;
            var resolution = SelectionMethodResolver.ResolveSelectionFilterMethodFromTargetOrParent(_target, _parent, methodName);
            if (resolution?.Method == null || resolution.Method.GetParameters().Length != 0
                || resolution.Method.Invoke(resolution.InvocationTarget, null) is not T value)
                throw new InvalidOperationException($"Selection configuration method '{methodName}' must be parameterless and return {typeof(T).Name}.");
            return value;
        }

        private IReadOnlyList<SelectionDropDown.Item> CreateItems(IEnumerable source, bool required)
        {
            var items = new List<SelectionDropDown.Item>();
            if (!required && (!_property.PropertyType.IsValueType || Nullable.GetUnderlyingType(_property.PropertyType) != null))
                items.Add(new SelectionDropDown.Item(string.Empty, null, null));
            foreach (var item in source ?? Array.Empty<object>())
            {
                if (item == null)
                    continue;
                var value = ReadMember(item, _combo.SelectionValueMember);
                if (value == null && required)
                    continue;
                var display = ReadMember(item, _ui?.SelectionDisplayMember)?.ToString();
                if (string.IsNullOrWhiteSpace(display))
                    display = item.ToString();
                items.Add(new SelectionDropDown.Item(display, value, null));
            }
            return items;
        }

        private static object ReadMember(object item, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return item;
            object value = item;
            foreach (var name in path.Split('.'))
            {
                if (value == null)
                    return null;
                var property = value.GetType().GetProperty(name)
                    ?? throw new InvalidOperationException($"Selection member '{path}' was not found.");
                value = property.GetValue(value);
            }
            return value;
        }

        private FrameworkElement BuildEditor(DropDownMode mode, bool password, IReadOnlyList<SelectionDropDown.Item> items)
        {
            var readOnly = _readOnly || FlowBlockUIAttributeHelper.IsDynamicallyReadOnly(_target, _ui);
            var binding = new Binding(_property.Name)
            {
                Source = _target,
                Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ValidatesOnDataErrors = true,
                ValidatesOnExceptions = true
            };
            FrameworkElement editor = null;
            var hasChoices = items.Any(item => item.Value != null);
            if (password || mode == DropDownMode.Suggestion)
            {
                editor = new TextBoxWithOptionalButtonsCreator(_window).CreateTextBoxWithOptionalButtons(
                    _property, _target, _property.Name, _ui, binding, readOnly, password);
                if (password || !hasChoices)
                    return editor;
            }

            var selector = new SelectionDropDownControl
            {
                ItemsSource = items,
                IsSuggestion = mode == DropDownMode.Suggestion,
                IsHitTestVisible = !readOnly,
                Focusable = !readOnly,
                IsTabStop = !readOnly
            };
            PropertyEditorBinding.Bind(selector, SelectionDropDownControl.SelectedValueProperty, binding, _target, _property.Name);
            var border = new Border { Child = selector };
            border.SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.EditorContainerStyle");
            if (editor == null)
                return border;
            border.Margin = new Thickness(0, 0, 0, 4);
            var panel = new DockPanel();
            DockPanel.SetDock(border, Dock.Top);
            panel.Children.Add(border);
            panel.Children.Add(editor);
            return panel;
        }
    }
}
