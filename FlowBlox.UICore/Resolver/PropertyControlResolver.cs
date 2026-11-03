using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Factory.PropertyView;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.PropertyView.Resolver;
using FlowBlox.UICore.ViewModels.PropertyView;
using FlowBlox.UICore.Utilities;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FlowBlox.UICore.Resolver
{
    public class PropertyControlResolver
    {
        private Window _window;
        private readonly object _parent;
        private TextBoxWithOptionalButtonsCreator _textBoxWithOptionalButtonsCreator;
        private SimplePropertyInputControlWithOptionalButtonsCreator _simplePropertyInputControlWithOptionalButtonsCreator;

        public PropertyControlResolver(Window window, object parent = null)
        {
            this._window = window;
            this._parent = parent;
            this._textBoxWithOptionalButtonsCreator = new TextBoxWithOptionalButtonsCreator(window);
            this._simplePropertyInputControlWithOptionalButtonsCreator = new SimplePropertyInputControlWithOptionalButtonsCreator(window);
        }

        public PropertyControlViewModel Resolve(
            PropertyInfo property, 
            object target, 
            bool contextIsReadOnly, 
            object preselectedInstance, 
            PropertyControlBindingContext bindingContext)
        {
            if (property == null) 
                throw new ArgumentNullException(nameof(property));

            if (target == null) 
                throw new ArgumentNullException(nameof(target));

            var displayAttribute = property.GetCustomAttribute<DisplayAttribute>();
            var uiAttribute = property.GetCustomAttribute<FlowBloxUIAttribute>();

            if (displayAttribute == null || (uiAttribute != null && !uiAttribute.Visible))
                return null;

            var displayName = FlowBloxResourceUtil.GetDisplayName(displayAttribute, false) ?? property.Name;

            string description = default;
            if (!string.IsNullOrEmpty(displayAttribute?.Description) && displayAttribute?.ResourceType != null)
                description = FlowBloxResourceUtil.GetLocalizedString(displayAttribute.Description, displayAttribute.ResourceType);

            var readOnly = contextIsReadOnly ||
                uiAttribute?.ReadOnly == true ||
                !property.CanWrite;

            var controlResult = CreateControl(property, target, displayName, uiAttribute, readOnly, preselectedInstance);

            bool isActive = true;
            var activationAttr = property.GetCustomAttribute<ActivationConditionAttribute>();
            if (activationAttr != null)
                isActive = activationAttr.IsActive(target);

            var labelSettings = GetLabelSettings(property, displayName, uiAttribute);

            var propertyControlViewModel = new PropertyControlViewModel()
            {
                PropertyName = property.Name,
                Target = target,
                UseLabel = labelSettings.UseLabel,
                Label = labelSettings.LabelText,
                Value = property.GetValue(target),
                ValueType = property.PropertyType,
                IsEnabled = !FlowBlockUIAttributeHelper.IsDynamicallyReadOnly(target, uiAttribute),
                Control = controlResult.FrameworkElement,
                NestedTransaction = controlResult.PropertyViewControlFactory as IPropertyViewNestedTransaction,
                IsActive = isActive,
                Maximize = uiAttribute?.Maximize == true,
                TooltipText = description
            };

            if (controlResult.PropertyViewControlFactory != null)
            {
                controlResult.PropertyViewControlFactory.AssociationBeforeLink += propertyControlViewModel.RelayAssociationBeforeLink;

                if (propertyControlViewModel.NestedTransaction != null)
                {
                    controlResult.PropertyViewControlFactory.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(IPropertyViewNestedTransaction.HasPendingChanges))
                            propertyControlViewModel.HasChanges = propertyControlViewModel.NestedTransaction.HasPendingChanges;
                    };
                }
            }

            bindingContext.Register(property, propertyControlViewModel);

            return propertyControlViewModel;
        }

        private (bool UseLabel, string LabelText) GetLabelSettings(
            PropertyInfo property,
            string displayName,
            FlowBloxUIAttribute uiAttribute)
        {
            // If DisplayLabel is explicitly set to false, never show a label.
            bool displayLabel = uiAttribute?.DisplayLabel ?? true;
            if (!displayLabel)
                return (false, string.Empty);

            return (true, displayName);
        }

        private PropertyControlWithFactoryResult CreateControl(
            PropertyInfo property, 
            object target, 
            string displayName, 
            FlowBloxUIAttribute uiAttribute, 
            bool readOnly, 
            object preselectedInstance)
        {
            var binding = new Binding(property.Name)
            {
                Source = target,
                Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ValidatesOnDataErrors = true,
                ValidatesOnExceptions = true
            };

            if (IsSupportedSimplePropertyType(property.PropertyType) ||
                EnumHelper.IsEnumType(property))
            {
                return new PropertyControlWithFactoryResult(
                    _simplePropertyInputControlWithOptionalButtonsCreator.CreateSimplePropertyInputControlWithOptionalButtons(
                        property,
                        target,
                        uiAttribute,
                        binding,
                        readOnly));
            }

            // Selection-Filter
            if (uiAttribute?.Factory == UIFactory.ComboBox)
            {
                var selectionMethodResolution = SelectionMethodResolver.ResolveSelectionFilterMethodFromTargetOrParent(
                    target,
                    _parent,
                    uiAttribute?.SelectionFilterMethod);

                if (selectionMethodResolution?.Method != null)
                {
                    var comboBox = new ComboBox
                    {
                        ItemsSource = SelectionItemsSourceResolver.ResolveItemsSource(selectionMethodResolution, property),
                        DisplayMemberPath = uiAttribute?.SelectionDisplayMember
                    };

                    comboBox.SelectionChanged += (s, e) => FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);

                    SetComboBoxReadOnly(comboBox, readOnly);
                    AttachSelectionFilterDependencyRefresh(comboBox, property, target, uiAttribute, selectionMethodResolution);

                    comboBox.SetBinding(ComboBox.SelectedValueProperty, binding);
                    return new PropertyControlWithFactoryResult(CreateEditorContainer(comboBox));
                }
            }

            if (uiAttribute?.Factory == UIFactory.Association)
            {
                AssociationControlFactory associationControlFactory = new AssociationControlFactory(_window, property, target, readOnly, _parent);
                return new PropertyControlWithFactoryResult(associationControlFactory.Create(), associationControlFactory);
            }

            if (uiAttribute?.Factory == UIFactory.GridView)
            {
                DataGridFactory dataGridFactory = new DataGridFactory(_window, property, target, readOnly, _parent);
                dataGridFactory.SetPreselectedInstance(preselectedInstance);
                return new PropertyControlWithFactoryResult(dataGridFactory.Create(), dataGridFactory);
            }

            if (uiAttribute?.Factory == UIFactory.ListView)
            {
                ListViewFactory listViewFactory = new ListViewFactory(_window, property, target, readOnly, _parent);
                listViewFactory.SetPreselectedInstance(preselectedInstance);
                return new PropertyControlWithFactoryResult(listViewFactory.Create(), listViewFactory);
            }

            if (uiAttribute?.Factory == UIFactory.ListViewSplitMode)
            {
                ListViewSplitModeFactory listViewSplitModeFactory = new ListViewSplitModeFactory(_window, property, target, readOnly, _parent);
                return new PropertyControlWithFactoryResult(listViewSplitModeFactory.Create(), listViewSplitModeFactory);
            }

            var textAttribute = property.GetCustomAttribute<FlowBloxTextBoxAttribute>();
            if (property.PropertyType == typeof(string) && textAttribute?.IsPassword == true)
            {
                var passwordBox = new PasswordBox
                {
                    IsEnabled = !readOnly,
                    Password = property.GetValue(target)?.ToString() ?? string.Empty
                };
                passwordBox.PasswordChanged += (s, e) =>
                {
                    if (readOnly)
                        return;

                    property.SetValue(target, passwordBox.Password);
                    FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
                };
                return new PropertyControlWithFactoryResult(passwordBox);
            }

            return new PropertyControlWithFactoryResult(
                _textBoxWithOptionalButtonsCreator
                    .CreateTextBoxWithOptionalButtons(property, target, displayName, uiAttribute, binding, readOnly));
        }

        private void AttachSelectionFilterDependencyRefresh(
            ComboBox comboBox,
            PropertyInfo property,
            object target,
            FlowBloxUIAttribute uiAttribute,
            SelectionMethodResolutionResult selectionMethodResolution)
        {
            if (string.IsNullOrWhiteSpace(uiAttribute?.SelectionFilterDependency))
                return;

            var dependencyResolution = SelectionFilterDependencyResolver.ResolveSelectionFilterDependencyFromTargetOrParent(
                target,
                _parent,
                uiAttribute.SelectionFilterDependency);

            if (dependencyResolution?.Property == null ||
                dependencyResolution.InvocationTarget is not INotifyPropertyChanged notifyPropertyChanged)
            {
                return;
            }

            PropertyChangedEventHandler handler = (s, e) =>
            {
                if (e.PropertyName != dependencyResolution.Property.Name)
                    return;

                comboBox.ItemsSource = SelectionItemsSourceResolver.ResolveItemsSource(selectionMethodResolution, property);
                FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
            };

            notifyPropertyChanged.PropertyChanged += handler;
        }

        private void Toggle_Toggled(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private static Border CreateEditorContainer(UIElement editor)
        {
            var container = new Border { Child = editor };
            container.SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.EditorContainerStyle");
            return container;
        }

        private void SetComboBoxReadOnly(ComboBox comboBox, bool readOnly)
        {
            comboBox.IsHitTestVisible = !readOnly;
            comboBox.Focusable = !readOnly;
            comboBox.IsTabStop = !readOnly;
        }

        private static bool IsSupportedSimplePropertyType(Type propertyType)
        {
            return propertyType == typeof(bool) ||
                   propertyType == typeof(bool?) ||
                   propertyType == typeof(int) ||
                   propertyType == typeof(int?) ||
                   propertyType == typeof(long) ||
                   propertyType == typeof(long?) ||
                   propertyType == typeof(float) ||
                   propertyType == typeof(float?) ||
                   propertyType == typeof(double) ||
                   propertyType == typeof(double?) ||
                   propertyType == typeof(DateTime) ||
                   propertyType == typeof(DateTime?);
        }
    }
}
