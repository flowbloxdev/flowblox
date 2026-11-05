using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Provider;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Views;
using FlowBlox.Views;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using WeifenLuo.WinFormsUI.Docking;

namespace FlowBlox.AppWindow.Contents
{
    public partial class ProjectPanel : DockContent
    {
        private const int RuntimeFocusUpdateThrottleMilliseconds = 50;

        private readonly ElementHost _elementHost;
        private readonly ProjectPanelWpfControl _projectPanelWpfControl;
        private readonly IRuntimeStateService _runtimeStateService;
        private readonly RuntimeFocusUpdateThrottler _runtimeFocusUpdateThrottler;
        private FlowBloxRuntime _runtime;
        private Thread _runtimeThread;
        private bool _disposed;

        internal ProjectPanelWpfControl WpfControl => _projectPanelWpfControl;

        public bool IsRuntimeActive => _runtimeStateService?.IsRuntimeActive ?? (_runtime?.Running == true && !_runtime.Aborted);
        public bool IsExternalProjectEditActive => _runtimeStateService?.IsExternalProjectEditActive == true;

        public string RuntimeLogfilePath => ((_runtimeStateService?.CurrentRuntime ?? _runtime) as FlowBloxRuntime)?.GetLogfilePath();

        internal void EnableGridUpdate()
        {
        }

        internal void DisableGridUpdate()
        {
        }

        public ProjectPanel()
        {
            Text = FlowBloxResourceUtil.GetLocalizedString("ProjectPanel_Text", typeof(FlowBloxMainUITexts));
            Name = Text;
            DockAreas = DockAreas.Document;
            Padding = new Padding(0, 25, 0, 0);

            _runtimeStateService = FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>();
            _projectPanelWpfControl = new ProjectPanelWpfControl();
            _runtimeFocusUpdateThrottler = new RuntimeFocusUpdateThrottler(
                this,
                _projectPanelWpfControl.MarkRuntimeFocus,
                RuntimeFocusUpdateThrottleMilliseconds);
            _projectPanelWpfControl.ViewModel.ExecuteRuntimeRequested += WpfProjectPanel_ExecuteRuntimeRequested;
            _projectPanelWpfControl.ViewModel.PauseRuntimeRequested += WpfProjectPanel_PauseRuntimeRequested;
            _projectPanelWpfControl.ViewModel.StopRuntimeRequested += WpfProjectPanel_StopRuntimeRequested;

            _elementHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Child = _projectPanelWpfControl
            };

            Controls.Add(_elementHost);
        }

        protected override void OnClosed(EventArgs e)
        {
            DisposeProjectPanelResources();

            base.OnClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeProjectPanelResources();

            base.Dispose(disposing);
        }

        private void DisposeProjectPanelResources()
        {
            if (_disposed)
                return;

            _disposed = true;
            _projectPanelWpfControl.ViewModel.ExecuteRuntimeRequested -= WpfProjectPanel_ExecuteRuntimeRequested;
            _projectPanelWpfControl.ViewModel.PauseRuntimeRequested -= WpfProjectPanel_PauseRuntimeRequested;
            _projectPanelWpfControl.ViewModel.StopRuntimeRequested -= WpfProjectPanel_StopRuntimeRequested;
            _projectPanelWpfControl.ViewModel.Dispose();
            _runtimeFocusUpdateThrottler.Dispose();
        }

        public void UpdateUI(bool gridUpdate = true, bool appWindowUpdate = false)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateUI(gridUpdate, appWindowUpdate)));
                return;
            }

            _projectPanelWpfControl.UpdateRuntimeState(
                IsRuntimeActive,
                _runtime?.Pause == true,
                IsExternalProjectEditActive);
            if (gridUpdate)
                _projectPanelWpfControl.RefreshProject();

            if (appWindowUpdate)
                AppWindow.Instance.UpdateUI();
        }

        internal void AfterProjectFullyInitialized() => _projectPanelWpfControl.RefreshProject();

        internal void OnAfterProjectCreated() => _projectPanelWpfControl.RefreshProject();

        internal void OnAfterProjectOpened(FlowBloxProject project) => _projectPanelWpfControl.RefreshProject();

        internal void OnAfterProjectClosed() => _projectPanelWpfControl.RefreshProject();

        internal void SyncNodeSizesToModel() => _projectPanelWpfControl.SyncNodeSizesToModel();

        internal void PrepareProjectForSave(FlowBloxProject project)
        {
            _projectPanelWpfControl.PrepareNodeLocationForStorage();
        }

        internal void RestoreProjectAfterSaveAttempt(FlowBloxProject project)
        {
            _projectPanelWpfControl.RestoreNodeLocationAfterStorage();
        }

        internal void SaveInnerPanelBitmap(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return;

            using var bitmap = new Bitmap(Math.Max(1, _elementHost.Width), Math.Max(1, _elementHost.Height));
            _elementHost.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(fileName, ImageFormat.Png);
        }

        internal void Undo()
        {
            var changelist = FlowBloxServiceLocator.Instance.GetService<FlowBloxProjectComponentProvider>()?.GetCurrentChangelist();
            if (changelist?.CanUndo != true)
                return;

            changelist.Undo();
            UpdateUI(appWindowUpdate: true);
        }

        internal void Redo()
        {
            var changelist = FlowBloxServiceLocator.Instance.GetService<FlowBloxProjectComponentProvider>()?.GetCurrentChangelist();
            if (changelist?.CanRedo != true)
                return;

            changelist.Redo();
            UpdateUI(appWindowUpdate: true);
        }

        private void WpfProjectPanel_ExecuteRuntimeRequested(object sender, EventArgs e)
            => ExecuteRuntime();

        private void WpfProjectPanel_PauseRuntimeRequested(object sender, EventArgs e)
            => PauseRuntime();

        private void WpfProjectPanel_StopRuntimeRequested(object sender, EventArgs e)
            => StopRuntime();

        private void StopRuntime()
        {
            if (_runtime == null)
                return;

            _runtime.CancelExecution(
                FlowBlox.Core.Models.Runtime.Debugging.RuntimeCancellationKind.UserCancellation,
                "Runtime execution was cancelled by the user.");
            _runtimeStateService?.AttachRuntime(_runtime);
            UpdateUI();
        }

        private void PauseRuntime()
        {
            if (_runtime == null)
                return;

            _runtime.Pause = true;
            _runtimeStateService?.AttachRuntime(_runtime);
            UpdateUI();
        }

        private void ExecuteRuntime()
        {
            if (FlowBloxProjectManager.Instance.ActiveProject == null)
                return;

            if (IsExternalProjectEditActive)
                return;

            if (IsRuntimeActive && _runtime?.Pause == true)
            {
                _runtime.Pause = false;
                _runtimeStateService?.AttachRuntime(_runtime);
                UpdateUI();
                return;
            }

            try
            {
                var runtime = new FlowBloxRuntime(FlowBloxProjectManager.Instance.ActiveProject);
                _runtimeThread = new Thread(new ThreadStart(runtime.Execute));
                _runtime = runtime;
                _runtime.Running = true;
                _runtimeStateService?.AttachRuntime(runtime);

                runtime.Finish += Runtime_Finish;
                runtime.FocusChanged += Runtime_FocusChanged;
                runtime.PauseContinue += Runtime_PauseContinue;

                AppWindow.Instance.OnBeforeRuntimeStarted(runtime);

                _runtimeThread.Start();
                UpdateUI();
            }
            catch (Exception ex)
            {
                FlowBloxMessageBox.Show(
                    this,
                    string.Format(
                        FlowBloxResourceUtil.GetLocalizedString("ProjectPanel_RuntimeStartFailed_Message", typeof(FlowBloxMainUITexts)),
                        ex.Message),
                    FlowBloxResourceUtil.GetLocalizedString("ProjectPanel_RuntimeStartFailed_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Info);
            }
        }

        private void Runtime_PauseContinue(bool isPaused) => UpdateUI();

        private void Runtime_FocusChanged(BaseFlowBlock flowBlock)
            => _runtimeFocusUpdateThrottler.Schedule(flowBlock);

        private void Runtime_Finish(object result)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new BaseRuntime.FinishedEventHandler(Runtime_Finish), result);
                return;
            }

            var resetNotificationsOnRuntimeFinish =
                FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.GridResetNotificationsOnRuntimeFinish)?.GetValueBoolean() ?? true;

            if (resetNotificationsOnRuntimeFinish)
                _projectPanelWpfControl.ResetNotifications(_runtime);

            _runtimeFocusUpdateThrottler.ClearPending();
            _projectPanelWpfControl.RefreshProject();

            if (result is Exception exception && exception is not RuntimeCancellationException)
            {
                FlowBloxMessageBox.Show(
                    this,
                    string.Format(
                        FlowBloxResourceUtil.GetLocalizedString("ProjectPanel_RuntimeAborted_Message", typeof(FlowBloxMainUITexts)),
                        exception,
                        Environment.NewLine),
                    FlowBloxResourceUtil.GetLocalizedString("ProjectPanel_RuntimeAborted_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Error);
            }

            _runtime = null;
            AppWindow.Instance.OnAfterRuntimeFinished();
            UpdateUI();
        }
    }
}
