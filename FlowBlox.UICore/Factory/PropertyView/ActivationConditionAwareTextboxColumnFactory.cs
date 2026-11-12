using ActivationConditionAwareTextboxColumnFactoryResources = FlowBlox.UICore.Resources.ClassResources.ActivationConditionAwareTextboxColumnFactory;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.Resources;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FlowBlox.UICore.Factory.PropertyView
{
    internal static class ActivationConditionAwareTextboxColumnFactory
    {
        private const double DefaultMinWidth = 140;

        public static DataGridTemplateColumn Create(
            string headerText,
            PropertyInfo property,
            ActivationConditionAttribute activationCondition,
            bool isReadOnly,
            FrameworkElementFactory editor)
        {
            editor.SetBinding(
                UIElement.VisibilityProperty,
                CreateActivationBinding(property, activationCondition, showWhenActive: true));
            var grid = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            grid.AppendChild(editor);
            grid.AppendChild(CreateInactiveCellText(property, activationCondition));
            var template = new DataTemplate { VisualTree = grid };
            return new DataGridTemplateColumn
            {
                Header = headerText,
                MinWidth = DefaultMinWidth,
                SortMemberPath = property.Name,
                IsReadOnly = isReadOnly,
                CellTemplate = template,
                CellEditingTemplate = template
            };
        }
        private static FrameworkElementFactory CreateInactiveCellText(
            PropertyInfo property,
            ActivationConditionAttribute activationCondition)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextProperty, FlowBloxResourceUtil.GetLocalizedString("NotApplicable", typeof(ActivationConditionAwareTextboxColumnFactoryResources)));
            text.SetValue(TextBlock.BackgroundProperty, new SolidColorBrush(Color.FromRgb(255, 232, 204)));
            text.SetValue(TextBlock.ForegroundProperty, Brushes.DarkSlateGray);
            text.SetValue(TextBlock.PaddingProperty, new Thickness(4, 2, 4, 2));
            text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            text.SetValue(UIElement.IsHitTestVisibleProperty, false);
            text.SetBinding(
                UIElement.VisibilityProperty,
                CreateActivationBinding(property, activationCondition, showWhenActive: false));
            return text;
        }

        private static MultiBinding CreateActivationBinding(
            PropertyInfo property,
            ActivationConditionAttribute activationCondition,
            bool showWhenActive)
        {
            var binding = new MultiBinding
            {
                Converter = new ActivationConditionVisibilityConverter(
                    activationCondition,
                    showWhenActive)
            };
            binding.Bindings.Add(new Binding("."));

            if (!string.IsNullOrWhiteSpace(activationCondition.MemberName))
                binding.Bindings.Add(new Binding(activationCondition.MemberName));

            var dependsOnProperty = property.GetCustomAttribute<DependsOnPropertyAttribute>();
            foreach (var memberName in dependsOnProperty?.GetMemberNames() ?? [])
                binding.Bindings.Add(new Binding(memberName));

            return binding;
        }

        private sealed class ActivationConditionVisibilityConverter : IMultiValueConverter
        {
            private readonly ActivationConditionAttribute _condition;
            private readonly bool _showWhenActive;

            public ActivationConditionVisibilityConverter(
                ActivationConditionAttribute condition,
                bool showWhenActive)
            {
                _condition = condition;
                _showWhenActive = showWhenActive;
            }

            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                var isActive = values.Length > 0 &&
                    values[0] != null &&
                    values[0] != DependencyProperty.UnsetValue &&
                    _condition.IsActive(values[0]);

                return isActive == _showWhenActive ? Visibility.Visible : Visibility.Collapsed;
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
                => throw new NotSupportedException();
        }
    }
}
