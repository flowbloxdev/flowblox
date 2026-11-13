using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.UICore.ViewModels;
using MahApps.Metro.Controls;
using System.Windows;

namespace FlowBlox.UICore.Views
{
    /// <summary>
    /// Interaktionslogik für GenerationView.xaml
    /// </summary>
    public partial class GenerationView : MetroWindow
    {
        public GenerationView(BaseFlowBlock currentFlowBlock)
        {
            InitializeComponent();
            var viewModel = (GenerationViewModel)DataContext;
            viewModel.OwnerWindow = this;
            viewModel.CurrentFlowBlock = currentFlowBlock;
            _ = viewModel.Generate();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
