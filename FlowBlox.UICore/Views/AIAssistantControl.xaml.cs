using System.Windows.Controls;
using FlowBlox.UICore.PopUp.Implementations;
using FlowBlox.UICore.ViewModels;

namespace FlowBlox.UICore.Views
{
    public partial class AIAssistantControl : UserControl
    {
        public event EventHandler? ConfigurationRequested;

        public AIAssistantControl()
        {
            InitializeComponent();
            SettingsButton.Click += (_, _) => ConfigurationRequested?.Invoke(this, EventArgs.Empty);
        }

        private void QuickStartButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is AIAssistantViewModel viewModel)
                new AiAssistantQuickStart().Show(viewModel.ChatViewModel, System.Windows.Window.GetWindow(this), force: true);
        }
    }
}
