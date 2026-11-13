using FlowBlox.Core.Util.Execution;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util;
using FlowBlox.UICore.PopUp.Views;
using System.Windows;

namespace FlowBlox.UICore.PopUp.Provider
{
    public abstract class ComponentPopupProviderBase<TTarget> : IComponentPopupProvider
    {
        public Type TargetType => typeof(TTarget);

        public virtual ComponentPopupEvent PopupEvent => ComponentPopupEvent.Open;

        public abstract string OptionKey { get; }

        protected abstract string WindowTitle { get; }

        protected abstract IReadOnlyList<ComponentPopupItem> CreateItems(TTarget target);

        public bool CanShowFor(object target, ComponentPopupEvent popupEvent)
        {
            return target is TTarget && popupEvent == this.PopupEvent;
        }

        /// <summary>
        /// Open guidance is listed automatically in Help. Instance-specific providers can opt out.
        /// </summary>
        public virtual bool IsQuickStart => PopupEvent == ComponentPopupEvent.Open;

        public virtual string QuickStartTitle => ExecutionPrerequisites.GetQuickStartName(typeof(TTarget), WindowTitle);

        /// <summary>
        /// Manual guidance is independent of a configured component and does not change onboarding state.
        /// Override CreateQuickStartItems when guidance normally depends on instance data.
        /// </summary>
        public Window CreateQuickStartWindow()
        {
            if (!IsQuickStart)
                return null;

            var items = CreateQuickStartItems();
            return items.Count == 0 ? null : CreateWindow(items, null);
        }

        protected virtual IReadOnlyList<ComponentPopupItem> CreateQuickStartItems() => CreateItems(default);

        public bool Show(object target, Window owner = null, bool force = false)
        {
            if (target is not TTarget typedTarget)
                return false;

            var options = FlowBloxOptions.GetOptionInstance();
            var option = EnsureOption(options);
            if (!force && option.GetValueBoolean())
                return false;

            var items = CreateItems(typedTarget);
            if (items.Count == 0)
                return false;

            var window = CreateWindow(items, owner);

            var completed = window.ShowDialog() == true;
            if (completed && !option.GetValueBoolean())
            {
                option.Value = bool.TrueString;
                options.Save();
            }

            return true;
        }

        protected virtual ComponentPopupWindow CreateWindow(IReadOnlyList<ComponentPopupItem> items, Window owner)
        {
            return new ComponentPopupWindow(WindowTitle, items)
            {
                Owner = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive),
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
            };
        }

        private OptionElement EnsureOption(FlowBloxOptions options)
        {
            var option = options.GetOption(OptionKey);
            if (option != null)
                return option;

            option = new OptionElement(
                OptionKey,
                bool.FalseString,
                "Tracks whether this component guidance has been completed.",
                OptionElement.OptionType.Boolean);

            options.OptionCollection[OptionKey] = option;
            options.Save();
            return option;
        }
    }
}
