using System.Reflection;
using FlowBlox.Core.Attributes;

namespace FlowBlox.Core.Extensions
{
    /// <summary>
    /// Resolves property UI metadata and its defaults for UI, runtime and AI consumers.
    /// Defaults belong to the attribute constructors; returned fallback instances are never shared.
    /// </summary>
    public static class FlowBloxUIAttributeExtensions
    {
        /// <summary>
        /// Returns the declared general UI settings or a new attribute containing their defaults.
        /// Does not determine whether a property should be rendered.
        /// </summary>
        public static FlowBloxUIAttribute GetFlowBloxUIAttribute(this PropertyInfo property)
        {
            return property.GetCustomAttribute<FlowBloxUIAttribute>(inherit: true)
                ?? new FlowBloxUIAttribute();
        }

        /// <summary>
        /// Returns object-management settings, including defaults when no settings are declared.
        /// </summary>
        public static FlowBloxAssociationAttribute GetFlowBloxAssociationAttribute(this PropertyInfo property)
        {
            return property.GetCustomAttribute<FlowBloxAssociationAttribute>(inherit: true)
                ?? new FlowBloxAssociationAttribute();
        }

        /// <summary>
        /// Returns the selectable-entry source, including empty defaults when none is declared.
        /// </summary>
        public static FlowBloxItemsSourceAttribute GetFlowBloxItemsSourceAttribute(this PropertyInfo property)
        {
            return property.GetCustomAttribute<FlowBloxItemsSourceAttribute>(inherit: true)
                ?? new FlowBloxItemsSourceAttribute();
        }

        /// <summary>
        /// Derives the editor from its annotation. Collection presentation takes precedence over
        /// object-management settings, which also configure collection item operations.
        /// </summary>
        public static UIFactory GetFlowBloxUIFactory(this PropertyInfo property)
        {
            if (property.IsDefined(typeof(FlowBloxDataGridAttribute), inherit: true))
                return UIFactory.GridView;

            var listView = property.GetCustomAttribute<FlowBloxListViewAttribute>(inherit: true);
            if (listView != null)
                return listView.SplitMode ? UIFactory.ListViewSplitMode : UIFactory.ListView;

            if (property.IsDefined(typeof(FlowBloxComboBoxAttribute), inherit: true))
                return UIFactory.ComboBox;

            if (property.IsDefined(typeof(FlowBloxAssociationAttribute), inherit: true))
                return UIFactory.Association;

            return UIFactory.Default;
        }
    }
}
