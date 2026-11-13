using System.Windows;

namespace FlowBlox.UICore.PopUp.Provider
{
    public interface IComponentPopupProvider
    {
        Type TargetType { get; }

        ComponentPopupEvent PopupEvent { get; }

        string OptionKey { get; }

        bool CanShowFor(object target, ComponentPopupEvent popupEvent);

        /// <summary>
        /// Includes target-independent guidance in the Help menu.
        /// </summary>
        bool IsQuickStart => false;

        string QuickStartTitle => TargetType.Name;

        Window CreateQuickStartWindow() => null;

        bool Show(object target, Window owner = null, bool force = false);
    }
}
