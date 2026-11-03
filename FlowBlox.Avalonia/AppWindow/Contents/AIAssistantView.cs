using FlowBlox.AvaloniaDesktop.Services;
#nullable enable annotations
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Json;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

using FlowBlox.AvaloniaDesktop.Interop.Wpf;

namespace FlowBlox.AppWindow.Contents
{
    public class AIAssistantView : WpfPanel
    {
        private readonly AIAssistantControl _assistantControl;
        private readonly AIAssistantViewModel _viewModel;
        private AiAssistantDebugView _debugWindow;

        public AIAssistantView()
        {
            Text = "AI Assistant";
            Name = nameof(AIAssistantView);

            _assistantControl = new AIAssistantControl();
            Child = _assistantControl;


            _assistantControl.ConfigurationRequested += AssistantControl_ConfigurationRequested;

            _viewModel = _assistantControl.DataContext as AIAssistantViewModel;
            if (_viewModel != null)
            {
                _viewModel.ConfigureProjectStateAccess(CaptureCurrentProjectState, RestoreProjectStateAsync);
                _viewModel.ChatViewModel.DebugToolsRequested += ChatViewModel_DebugToolsRequested;
            }
        }

        internal void AfterProjectFullyInitialized()
        {
            _viewModel?.ResetForProjectInitialization();
        }

        internal void ConfigureBeforeAutomaticLayoutAdjustment(Action beforeAutomaticLayoutAdjustment)
        {
            _viewModel?.ConfigureBeforeAutomaticLayoutAdjustment(beforeAutomaticLayoutAdjustment);
        }

        internal void ShowDebugTools()
        {
            if (_viewModel == null)
                return;

            if (_debugWindow != null)
            {
                _debugWindow.Activate();
                return;
            }

            _debugWindow = new AiAssistantDebugView(_viewModel.ChatViewModel.CreateDebugViewModel());
            _debugWindow.Closed += (_, _) => _debugWindow = null;
            var owner = AppWindow.Instance;
            if (owner != null)
                new System.Windows.Interop.WindowInteropHelper(_debugWindow).Owner = owner.Handle;
            _debugWindow.Show();
        }

        private void ChatViewModel_DebugToolsRequested(object sender, EventArgs e) => ShowDebugTools();

        private AIAssistantProjectStateSnapshot? CaptureCurrentProjectState()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            if (project == null)
                return null;

            project.RefreshOrderedTopLevelCollectionsForSerialization();

            return new AIAssistantProjectStateSnapshot
            {
                ProjectGuid = project.ProjectGuid,
                ProjectName = project.ProjectName ?? string.Empty,
                ProjectFilePath = project.ProjectFilePath,
                ProjectJson = JsonConvert.SerializeObject(project, JsonSettings.ProjectExport()),
                ExtensionsJson = JsonConvert.SerializeObject(project.Extensions ?? new()),
                ProjectSpaceGuid = project.ProjectSpaceGuid ?? string.Empty,
                ProjectSpaceVersion = project.ProjectSpaceVersion,
                ProjectSpaceEndpointUri = project.ProjectSpaceEndpointUri ?? string.Empty
            };
        }

        private async Task<bool> RestoreProjectStateAsync(AIAssistantProjectStateSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ProjectJson))
                return false;

            try
            {
                var restoredProject = FlowBloxProject.FromJsonContents(
                    snapshot.ProjectJson,
                    snapshot.ExtensionsJson,
                    string.IsNullOrWhiteSpace(snapshot.ProjectSpaceGuid) ? null : snapshot.ProjectSpaceGuid,
                    snapshot.ProjectSpaceVersion,
                    string.IsNullOrWhiteSpace(snapshot.ProjectSpaceEndpointUri) ? null : snapshot.ProjectSpaceEndpointUri,
                    string.IsNullOrWhiteSpace(snapshot.ProjectFilePath) ? null : snapshot.ProjectFilePath);

                return await AppWindow.Instance.RestoreProjectStateWithoutConfirmationAsync(restoredProject);
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _debugWindow?.Close();
                _assistantControl.ConfigurationRequested -= AssistantControl_ConfigurationRequested;
                if (_viewModel != null)
                {
                    _viewModel.ChatViewModel.DebugToolsRequested -= ChatViewModel_DebugToolsRequested;
                    _viewModel.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FlowBloxServiceLocator.Instance.RegisterServicesFromCurrentAppDomain();
            FlowBloxOptions.GetOptionInstance().InitDefaults(false);
        }

        private void AssistantControl_ConfigurationRequested(object sender, EventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AssistantControl_ConfigurationRequested(sender, e)));
                return;
            }

            var configuration = _viewModel.GetConfiguration(out var loadError);
            if (!string.IsNullOrWhiteSpace(loadError))
            {
                DesktopDialogs.Messages.Show(loadError,
                    "AI Assistant Configuration",
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Warning);
                return;
            }

            var propertyWindow = new FlowBlox.UICore.Views.PropertyWindow(new PropertyWindowArgs(
                configuration,
                readOnly: false,
                canSave: true,
                detached: true))
            {
                Title = "AI Assistant Configuration",
                Height = 760,
                Width = 980
            };

            var owner = AppWindow.Instance;
            WpfDialogHost.ShowDialog(propertyWindow, owner);

            if (propertyWindow.DialogResult != true)
            {
                return;
            }

            if (!_viewModel.SaveConfiguration(configuration, out var saveError))
            {
                DesktopDialogs.Messages.Show(saveError,
                    "AI Assistant Configuration",
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Error);
            }
        }

    }
}
