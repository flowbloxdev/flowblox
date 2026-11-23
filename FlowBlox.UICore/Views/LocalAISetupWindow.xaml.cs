using System.ComponentModel;
using System.Windows;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Project;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.ViewModels.LocalAI;
using FlowBlox.UICore.ViewModels.PropertyView;
using MahApps.Metro.Controls;

namespace FlowBlox.UICore.Views;

public partial class LocalAISetupWindow : MetroWindow
{
    private LocalAISetupViewModel ViewModel => (LocalAISetupViewModel)DataContext;

    public LocalAISetupWindow(FlowBloxProject project, BaseFlowBlock block)
    {
        InitializeComponent();
        DataContext = new LocalAISetupViewModel(
            project, block, FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>());
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 60);
        Closing += Window_Closing;
    }

    private async void Configure_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ConfigureAsync();
        var owner = Owner as PropertyWindow;
        var properties = (owner?.DataContext as PropertyWindowViewModel)?.PropertyViewModel;
        if (ViewModel.IsCompleted && properties != null)
            await properties.RefreshAfterAssistantAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e) => e.Cancel = ViewModel.IsBusy;
}
