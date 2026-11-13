using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;
using FlowBlox.UICore.PopUp.Views;
using System.Windows;

namespace FlowBlox.UICore.PopUp.Implementations
{
    public static class IterationContextQuickStart
    {
        public static IReadOnlyList<ComponentPopupItem> CreateItems() =>
            Enumerable.Range(1, 5)
                .Select(step => new ComponentPopupItem(
                    Text($"Step{step}_Headline"),
                    Text($"Step{step}_Description"),
                    PopUpImageResourceHelper.GetImageSource(
                        IterationContextPopUpImages.ResourceManager,
                        $"IterationContextQuickStart_{step}")))
                .ToList();

        public static void Show(Window? owner)
        {
            var window = new ComponentPopupWindow(Text("Window_Title"), CreateItems())
            {
                Owner = owner,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen
            };
            window.ShowDialog();
        }

        private static string Text(string key) =>
            FlowBloxResourceUtil.GetLocalizedString(key, typeof(IterationContextPopUpTexts));
    }
}
