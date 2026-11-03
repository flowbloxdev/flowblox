using FlowBlox.AvaloniaDesktop.Resources;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;


namespace FlowBlox.AppWindow.Contents
{
    public class RuntimeView : WpfPanel
    {
        private readonly RuntimeViewControl _runtimeViewControl;
        private readonly RuntimeViewModel _viewModel;

        public RuntimeView()
        {
            Text = FlowBloxResourceUtil.GetLocalizedString("RuntimeView_Text", typeof(FlowBloxMainUITexts));
            Name = Text;

            _runtimeViewControl = new RuntimeViewControl();
            _viewModel = _runtimeViewControl.DataContext as RuntimeViewModel;
            Child = _runtimeViewControl;


        }

        internal void InitializeRuntime(BaseRuntime runtime)
        {
            _runtimeViewControl.InitializeRuntime(runtime);
        }

        internal void Append(string message, FlowBloxLogLevel logLevel)
        {
            _runtimeViewControl.Append(message, logLevel);
        }

        internal void ContinueExecutionByUser() => _runtimeViewControl.ContinueExecutionByUser();

        internal void PauseExecutionByUser() => _runtimeViewControl.PauseExecutionByUser();

        internal void StopExecutionByUser() => _runtimeViewControl.StopExecutionByUser();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _viewModel?.Dispose();

            base.Dispose(disposing);
        }
    }
}