using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBloxTest.Views;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.IntegrationTests)]
public sealed class ExternalProjectExecutionConfirmationTests
{
    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApprovalButtons_KeepAllowOnceOnTheRightAndRememberOnlyAlwaysAllow(bool alwaysAllow)
    {
        var window = new ExternalProjectExecutionConfirmationWindow("", "Review execution");
        Exception? failure = null;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var once = (Button)window.FindName("AllowOnceButton");
                var always = (Button)window.FindName("AlwaysAllowButton");
                Assert.IsTrue(once.IsDefault);
                Assert.IsFalse(always.IsDefault);
                Assert.IsTrue(always.TranslatePoint(new Point(), window).X < once.TranslatePoint(new Point(), window).X);
                Assert.IsTrue(always.TranslatePoint(new Point(), window).X >= 0);
                Assert.IsTrue(once.TranslatePoint(new Point(once.ActualWidth, 0), window).X <= window.ActualWidth);
                (alwaysAllow ? always : once).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex)
            {
                failure = ex;
                window.Close();
            }
        }));
        try
        {
            var result = window.ShowDialog();
            if (failure != null)
                throw failure;
            Assert.AreEqual(true, result);
            Assert.AreEqual(alwaysAllow, window.RememberProjectApproval);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Parameters_StartCollapsedAndOfferSelectableReadOnlyValuesAndFullContent()
    {
        var value = string.Join(Environment.NewLine, Enumerable.Repeat("DELETE FROM Customers; -- review this statement", 25));
        var window = new ExternalProjectExecutionConfirmationWindow("", "Review the SQL", new Dictionary<string, string>
        {
            ["Sql"] = value
        });
        try
        {
            window.Show();
            var expander = Descendants(window).OfType<Expander>().Single();
            Assert.IsFalse(expander.IsExpanded);
            expander.IsExpanded = true;
            window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            window.UpdateLayout();
            var grid = (DataGrid)window.FindName("InputParametersGrid");
            Assert.IsTrue(grid.IsReadOnly);
            Assert.IsFalse(grid.CanUserAddRows);
            Assert.IsFalse(grid.CanUserDeleteRows);
            var item = (KeyValuePair<string, string>)grid.Items[0];
            Assert.AreEqual("Sql", item.Key);
            Assert.AreEqual(value, item.Value);
            grid.ScrollIntoView(item);
            grid.UpdateLayout();
            window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
            var editor = Descendants(grid).OfType<TextBox>().Single(box => box.Text == value);
            Assert.IsTrue(editor.IsReadOnly);
            editor.SelectAll();
            Assert.AreEqual(value, editor.SelectedText);

            Exception? failure = null;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var contentWindow = window.OwnedWindows.OfType<EditContentView>().Single();
                try
                {
                    var model = (EditContentViewModel)contentWindow.DataContext;
                    Assert.IsTrue(model.IsReadOnly);
                    Assert.AreEqual(value, contentWindow.ContentText);
                    model.SaveCommand.Execute(null);
                    Assert.IsTrue(contentWindow.IsVisible);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    contentWindow.Close();
                }
            }));
            Descendants(grid).OfType<Button>()
                .Single(button => button.Content is MahApps.Metro.IconPacks.PackIconMaterial icon &&
                    icon.Kind == MahApps.Metro.IconPacks.PackIconMaterialKind.EyeOutline)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (failure != null)
                throw failure;
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }
}
