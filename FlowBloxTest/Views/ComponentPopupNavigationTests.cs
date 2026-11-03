using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Views;
using MahApps.Metro.IconPacks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FlowBloxTest.Views;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class ComponentPopupNavigationTests
{
    [STATestMethod]
    public void BackNavigation_RestoresPagesAndUpdatesNavigationAtBothEnds()
    {
        var window = new ComponentPopupWindow("Navigation", new[]
        {
            new ComponentPopupItem("First", "**First content**", null!),
            new ComponentPopupItem("Second", "Second content", null!),
            new ComponentPopupItem("Third", "Third content", null!)
        });
        try
        {
            var back = (Button)window.FindName("PreviousButton");
            var next = (Button)window.FindName("NextButton");
            var headline = (TextBlock)window.FindName("HeadlineTextBlock");
            var markdown = (MdXaml.MarkdownScrollViewer)window.FindName("DescriptionMarkdown");
            var nextIcon = (PackIconMaterial)window.FindName("NextButtonIcon");

            Assert.AreEqual(Visibility.Collapsed, back.Visibility);
            back.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual("First", headline.Text);

            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual("Second", headline.Text);
            Assert.AreEqual(Visibility.Visible, back.Visibility);
            next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual("Third content", markdown.Markdown);
            Assert.AreEqual(PackIconMaterialKind.Check, nextIcon.Kind);

            back.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual("Second content", markdown.Markdown);
            Assert.AreEqual(PackIconMaterialKind.ArrowRight, nextIcon.Kind);
            back.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.AreEqual("First", headline.Text);
            Assert.AreEqual("**First content**", markdown.Markdown);
            Assert.AreEqual(Visibility.Collapsed, back.Visibility);
        }
        finally
        {
            window.Close();
        }
    }
}
