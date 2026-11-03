using FlowBlox.AvaloniaDesktop.Resources;
#nullable enable annotations
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;
using System;


namespace FlowBlox.AppWindow.Contents
{
    public class FieldView : WpfPanel
    {
        private readonly FieldViewControl _fieldViewControl;
        private readonly FieldViewModel? _viewModel;

        public FieldView()
        {
            Text = FlowBloxResourceUtil.GetLocalizedString("FieldView_Text", typeof(FlowBloxMainUITexts));
            Name = Text;

            _fieldViewControl = new FieldViewControl();
            Child = _fieldViewControl;



            _viewModel = _fieldViewControl.DataContext as FieldViewModel;
        }

        internal void AfterProjectFullyInitialized()
        {
            _fieldViewControl.AfterProjectFullyInitialized();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _viewModel?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
