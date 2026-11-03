using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.UICore.ViewModels.ComponentLibrary;
using FlowBlox.UICore.Views;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using System;


namespace FlowBlox.AppWindow.Contents
{
    public class ComponentLibraryView : WpfPanel
    {
        private readonly ComponentLibraryViewControl _componentLibraryViewControl;
        private readonly ComponentLibraryViewModel _viewModel;

        public ComponentLibraryView()
        {
            Text = FlowBlox.UICore.Resources.ComponentLibraryView.Title;
            Name = nameof(ComponentLibraryView);

            _componentLibraryViewControl = new ComponentLibraryViewControl();
            _viewModel = _componentLibraryViewControl.ViewModel;
            if (_viewModel != null)
                _viewModel.ManageExtensionsRequested += ViewModel_ManageExtensionsRequested;

            Child = _componentLibraryViewControl;


        }

        internal void UpdateUI()
        {
        }

        private void ViewModel_ManageExtensionsRequested(object sender, EventArgs e)
        {
            FlowBloxProject project = FlowBloxProjectManager.Instance.ActiveProject;
            var appWindow = AppWindow.Instance;
            var dialog = project == null
                ? new ExtensionsWindow()
                : new ExtensionsWindow(
                    project,
                    appWindow.PrepareProjectForSave,
                    appWindow.RestoreProjectAfterSaveAttempt);
            var owner = AppWindow.Instance;
            WpfDialogHost.ShowDialog(dialog, owner);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _viewModel != null)
            {
                _viewModel.ManageExtensionsRequested -= ViewModel_ManageExtensionsRequested;
                _viewModel.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
