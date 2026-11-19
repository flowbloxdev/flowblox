using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Util.Projects;
using FlowBlox.Core.Util.Resources;
using MahApps.Metro.IconPacks;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.UICore.Views;

public partial class ProjectOutputsView : UserControl
{
    private Dictionary<string, List<ProjectOutputDatasetDto>> _outputs = new();

    public ProjectOutputsView()
    {
        InitializeComponent();
    }

    public void SetOutputs(Dictionary<string, List<ProjectOutputDatasetDto>>? outputs)
    {
        _outputs = outputs ?? new();
        OutputNames.ItemsSource = _outputs.Keys.ToList();
        OutputNames.SelectedIndex = _outputs.Count > 0 ? 0 : -1;
        RefreshOutput();
    }

    private void OutputNames_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshOutput();

    private void RefreshOutput()
    {
        if (ResultFields == null)
            return;

        ResultFields.Children.Clear();
        ResultsGrid.ItemsSource = null;
        ResultsGrid.Columns.Clear();
        var table = OutputNames.SelectedItem is string name && _outputs.TryGetValue(name, out var datasets)
            ? ProjectOutputTable.Create(datasets)
            : new DataTable();
        EmptyHint.Visibility = table.Rows.Count == 0 || table.Columns.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SingleResult.Visibility = table.Rows.Count == 1 && table.Columns.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultsGrid.Visibility = table.Rows.Count > 1 && table.Columns.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (DataColumn column in table.Columns)
        {
            if (table.Rows.Count == 1)
            {
                ResultFields.Children.Add(new Border
                {
                    Style = (Style)FindResource("PropertyView.LabelBorderStyle"),
                    Child = new TextBlock
                    {
                        Text = column.ColumnName,
                        Style = (Style)FindResource("PropertyView.LabelTextStyle")
                    }
                });
                ResultFields.Children.Add(new TextBox
                {
                    Text = Convert.ToString(table.Rows[0][column]),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MinHeight = 80,
                    MaxHeight = 320,
                    Margin = new Thickness(0, 0, 0, 12)
                });
            }
            else
            {
                ResultsGrid.Columns.Add(CreateColumn(column));
            }
        }
        ResultsGrid.ItemsSource = table.DefaultView;
    }

    private DataGridColumn CreateColumn(DataColumn column)
    {
        var cell = new FrameworkElementFactory(typeof(DockPanel));
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(DockPanel.DockProperty, Dock.Right);
        button.SetValue(FrameworkElement.ToolTipProperty, FlowBloxResourceUtil.GetLocalizedString("ViewContent", typeof(Texts)));
        button.SetBinding(FrameworkElement.TagProperty, new Binding($"[{column.Ordinal}]") { Mode = BindingMode.OneWay });
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler(ViewContent_Click));
        var icon = new FrameworkElementFactory(typeof(PackIconMaterial));
        icon.SetValue(PackIconMaterial.KindProperty, PackIconMaterialKind.TextBoxSearchOutline);
        icon.SetValue(FrameworkElement.WidthProperty, 14.0);
        icon.SetValue(FrameworkElement.HeightProperty, 14.0);
        button.AppendChild(icon);
        cell.AppendChild(button);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding($"[{column.Ordinal}]") { Mode = BindingMode.OneWay });
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 6, 0));
        cell.AppendChild(text);
        return new DataGridTemplateColumn
        {
            Header = column.ColumnName,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 140,
            ClipboardContentBinding = new Binding($"[{column.Ordinal}]") { Mode = BindingMode.OneWay },
            CellTemplate = new DataTemplate { VisualTree = cell }
        };
    }

    private void ViewContent_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var view = new EditContentView(Convert.ToString(button.Tag) ?? string.Empty, isReadOnly: true)
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        view.ShowDialog();
    }
}
