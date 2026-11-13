using System.Reflection;
using System.Windows;
using FlowBlox.UICore.ViewModels;
using MahApps.Metro.Controls;

namespace FlowBlox.UICore.Views;

public partial class AboutWindow : MetroWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutViewModel(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();
}
