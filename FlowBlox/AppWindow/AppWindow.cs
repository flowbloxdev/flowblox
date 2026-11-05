using FlowBlox.AppWindow.Contents;
using FlowBlox.AppWindow.Handler;
using FlowBlox.AppWindow.RecentProjects;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.Interceptors;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.ObjectManager;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Services;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Interfaces;
using FlowBlox.Services;
using FlowBlox.UICore.Models;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Factory;
using FlowBlox.UICore.ViewModels.PSProjects;
using FlowBlox.UICore.Views;
using FlowBlox.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static FlowBlox.Core.Interceptors.RuntimeBacktraceInterceptor;
using FlowBlox.Provider;
using FlowBlox.Styles;
using FlowBlox.Util.Controls;
using FlowBlox.Util.WPF;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.Core.Services.Notifications;

namespace FlowBlox.AppWindow
{
    public partial class AppWindow : Form
    {
        private const string UpdateDownloadDirectoryName = "updates";
        private const string UpdateNotificationId = "app.update.available";
        private const string UpdateStatusNotificationId = "app.update.status";
        private const string ProjectGuidChangedNotificationId = "project.guid.changed";
        private const string CreatingProjectOverlayText = "Creating project...";
        private const string LoadingProjectOverlayText = "Loading project...";
        private const string RestoringProjectStateOverlayText = "Restoring state...";
        private static readonly TimeSpan BeforeProjectRestoreUiDelay = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan AfterProjectActivationUiDelay = TimeSpan.FromSeconds(2.5f);
        private static readonly TimeSpan DefaultNotificationLifetime = TimeSpan.FromSeconds(15);
        private static AppWindow _appWindow;
        public static AppWindow Instance
        {
            get
            {
                if (_appWindow == null)
                    _appWindow = new AppWindow();

                return _appWindow;
            }
        }

        public static void InitApp()
        {
            foreach (var assemblyPreloader in FlowBloxServiceLocator.Instance.GetServices<IAssemblyPreloader>())
            {
                assemblyPreloader.PreloadReferencedAssemblies();
            }
        }

        private ProjectPanel _dockContentProjectPanel;

        private string _recentProjectPath;
        private string _recentProjectSpaceGuid;
        private ComponentLibraryView _componentLibraryPanel;
        private FieldView _fieldViewPanel;
        private ManagedObjectsView _managedObjectsViewPanel;
        private TestView _testViewPanel;
        private Contents.RuntimeView _runtimeViewPanel;
        private Contents.ProblemsView _problemsViewPanel;
        private AIAssistantView _aiAssistantViewPanel;
        private bool _defaultPaneActivationApplied;
        private bool _isProjectLoading;
        private WaitOverlayWindow _waitOverlayWindow;
        private readonly IAppNotificationService _appNotificationService;
        private readonly IFlowBloxInstallerService _flowBloxInstallerService;
        private readonly IRuntimeStateService _runtimeStateService;
        private bool _isNotificationActionRunning;
        private string _downloadedInstallerPath;
        private string _installerPathToLaunchOnClose;
        private NotificationActionLabelStyleHandler _notificationActionLabelStyleHandler;

        private FlowBloxProjectComponentProvider _componentProvider;
        private ProjectChangelist _subscribedChangelist;
        private FlowBloxProject _subscribedProjectForGuidChanges;

        public AppWindow()
        {
            InitializeComponent();
            FlowBloxStyle.ApplyStyle(this);
            FlowBloxUILocalizationUtil.Localize(this);
            ApplyExampleProjectMenuIcon();

            _componentProvider = FlowBloxServiceLocator.Instance.GetService<FlowBloxProjectComponentProvider>();
            _appNotificationService = FlowBloxServiceLocator.Instance.GetService<IAppNotificationService>();
            _flowBloxInstallerService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxInstallerService>();
            _runtimeStateService = FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>();
            _appNotificationService.NotificationsChanged += AppNotificationService_NotificationsChanged;
            _notificationActionLabelStyleHandler = new NotificationActionLabelStyleHandler(lblNotificationAction, statusStrip);
            _notificationActionLabelStyleHandler.Register();
            this.Resize += (_, __) => PositionProjectLoadingOverlay();
            this.Move += (_, __) => PositionProjectLoadingOverlay();
            FlowBloxProjectManager.Instance.ProjectChanged += ProjectManager_ProjectChanged;

            RefreshRecentProjectsMenu();
            SubscribeCurrentChangelist();
            SubscribeCurrentProjectGuidChanges();
            this.UpdateUI();
            this.Size = new Size(1420, 1000);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            FlowBloxProjectManager.Instance.ProjectChanged -= ProjectManager_ProjectChanged;
            UnsubscribeCurrentChangelist();
            UnsubscribeCurrentProjectGuidChanges();
            _notificationActionLabelStyleHandler?.Dispose();
            base.OnFormClosed(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_componentLibraryPanel?.ProcessCmdKey(ref msg, keyData) == true)
                return true;

            if (ProcessProjectPanelWpfCmdKey(keyData))
                return true;

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private ProjectPanelWpfControl ProjectPanelWpfControl => _dockContentProjectPanel?.WpfControl;

        private bool ProcessProjectPanelWpfCmdKey(Keys keyData)
        {
            var projectPanelWpfControl = ProjectPanelWpfControl;
            if (projectPanelWpfControl == null)
                return false;

            if (projectPanelWpfControl.IsTextInputFocusActive)
                return false;

            if (dockPanel?.ActiveContent != _dockContentProjectPanel && !projectPanelWpfControl.CanHandleHostedShortcut)
                return false;

            switch (keyData)
            {
                case Keys.Control | Keys.Shift:
                case Keys.Control | Keys.Shift | Keys.ControlKey:
                case Keys.Control | Keys.Shift | Keys.ShiftKey:
                    projectPanelWpfControl.ActivateTemporaryConnectionShortcut();
                    return true;
                case Keys.Escape:
                    return projectPanelWpfControl.ExecuteEscapeShortcut();
                case Keys.Delete:
                    return projectPanelWpfControl.ExecuteDeleteShortcut();
                case Keys.Control | Keys.C:
                    CopyProjectPanelWpfSelection();
                    return true;
                case Keys.Control | Keys.V:
                    projectPanelWpfControl.PasteSelection();
                    return true;
                case Keys.Control | Keys.Left:
                    return projectPanelWpfControl.ExecuteSelectionShortcut(System.Windows.Input.Key.Left);
                case Keys.Control | Keys.Right:
                    return projectPanelWpfControl.ExecuteSelectionShortcut(System.Windows.Input.Key.Right);
                case Keys.Control | Keys.Up:
                    return projectPanelWpfControl.ExecuteSelectionShortcut(System.Windows.Input.Key.Up);
                case Keys.Control | Keys.Down:
                    return projectPanelWpfControl.ExecuteSelectionShortcut(System.Windows.Input.Key.Down);
                case Keys.Control | Keys.A:
                    return projectPanelWpfControl.ExecuteSelectionShortcut(System.Windows.Input.Key.A);
                default:
                    return false;
            }
        }

        public bool IsRuntimeActive => _runtimeStateService?.IsRuntimeActive ?? _dockContentProjectPanel?.IsRuntimeActive == true;
        public bool IsProjectEditingReadOnly => IsRuntimeActive || _runtimeStateService?.IsExternalProjectEditActive == true;

        private string _runtimeLogfilePath;
        public string RuntimeLogfilePath
        {
            get
            {
                var runtimeLogfilePath = _dockContentProjectPanel?.RuntimeLogfilePath;
                if (!string.IsNullOrEmpty(runtimeLogfilePath))
                    _runtimeLogfilePath = runtimeLogfilePath;
                return _runtimeLogfilePath;
            }
        }

        private void ProjectManager_ProjectChanged(object sender, ProjectChangedEventArgs e)
        {
            SubscribeCurrentChangelist();
            SubscribeCurrentProjectGuidChanges();
            UpdateUI();
        }

        private void SubscribeCurrentProjectGuidChanges()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            if (ReferenceEquals(_subscribedProjectForGuidChanges, project))
                return;

            UnsubscribeCurrentProjectGuidChanges();
            _subscribedProjectForGuidChanges = project;
            if (_subscribedProjectForGuidChanges != null)
                _subscribedProjectForGuidChanges.ProjectGuidChanged += Project_ProjectGuidChanged;
        }

        private void UnsubscribeCurrentProjectGuidChanges()
        {
            if (_subscribedProjectForGuidChanges == null)
                return;

            _subscribedProjectForGuidChanges.ProjectGuidChanged -= Project_ProjectGuidChanged;
            _subscribedProjectForGuidChanges = null;
        }

        private void Project_ProjectGuidChanged(object sender, ProjectGuidChangedEventArgs e)
        {
            var message = string.Format(
                FlowBloxResourceUtil.GetLocalizedString(
                    "AppWindow_Notification_ProjectGuidChanged_Message",
                    typeof(FlowBloxMainUITexts)),
                e.NewProjectGuid);

            _appNotificationService.Publish(new AppNotification
            {
                Id = ProjectGuidChangedNotificationId,
                Message = message,
                ActionText = string.Empty,
                ActionType = AppNotificationActionType.None,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(DefaultNotificationLifetime)
            });
        }

        private void SubscribeCurrentChangelist()
        {
            var changelist = _componentProvider.GetCurrentChangelist();
            if (ReferenceEquals(_subscribedChangelist, changelist))
                return;

            UnsubscribeCurrentChangelist();
            _subscribedChangelist = changelist;
            if (_subscribedChangelist != null)
                _subscribedChangelist.Changed += Changelist_Changed;
        }

        private void UnsubscribeCurrentChangelist()
        {
            if (_subscribedChangelist == null)
                return;

            _subscribedChangelist.Changed -= Changelist_Changed;
            _subscribedChangelist = null;
        }

        private void Changelist_Changed(object sender, EventArgs e)
        {
            UpdateUI();
        }

        public void UpdateUI()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(UpdateUI));
                return;
            }

            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var isRuntimeActive = false;
            if (_dockContentProjectPanel?.IsRuntimeActive == true)
                isRuntimeActive = true;

            var isProjectActive = project != null;

            dockPanel.Visible = isProjectActive && !_isProjectLoading;
            BackColor = isProjectActive && !_isProjectLoading ?
                Color.FromKnownColor(KnownColor.Control) :
                Color.FromArgb(52, 52, 52);

            itmCreateProject.Enabled = !isRuntimeActive;
            itmOpenProject.Enabled = !isRuntimeActive;
            itmOpenExampleProject.Enabled = !isRuntimeActive;
            itmRecentProjects.Enabled = !isRuntimeActive && itmRecentProjects.DropDownItems.Count > 0;
            itmCloseProject.Enabled = isProjectActive && !isRuntimeActive;
            itmUserFields.Enabled = isProjectActive;
            itmManageInputFiles.Enabled = isProjectActive;

            itmEditProject.Enabled = isProjectActive && (!isRuntimeActive);
            var isDistributedDataProject = IsActiveProjectFromDistributedData();
            itmSaveProject.Enabled = isProjectActive && !isRuntimeActive && !isDistributedDataProject;
            itmSaveAs.Enabled = isProjectActive
                && !isRuntimeActive
                && !string.IsNullOrWhiteSpace(_recentProjectPath);

            itmDockablePanels.Enabled = isProjectActive;
            itmResetDockablePanels.Enabled = isProjectActive;


            itmOpenRuntimeLogDirectory.Enabled = !string.IsNullOrEmpty(RuntimeLogfilePath);
            itmOpenProjectInputDir.Enabled = isProjectActive;
            itmOpenProjectOutputDir.Enabled = isProjectActive;

            itmSaveToProjectSpace.Enabled = isProjectActive && !isRuntimeActive && !isDistributedDataProject;

            var changelist = _componentProvider.GetCurrentChangelist();
            this.itmUndo.Enabled = (changelist != null) && (!isRuntimeActive) && changelist.CanUndo;
            this.itmRedo.Enabled = (changelist != null) && (!isRuntimeActive) && changelist.CanRedo;

            this._dockContentProjectPanel?.UpdateUI();
            this._componentLibraryPanel?.UpdateUI();
            this._managedObjectsViewPanel?.UpdateUI();
            this._testViewPanel?.UpdateUI();

            if (isRuntimeActive)
                BeginInvoke(new MethodInvoker(ActivateRuntimeViewPanel));
        }

        private void ActivateRuntimeViewPanel()
        {
            if (_runtimeViewPanel == null || _runtimeViewPanel.IsDisposed)
                return;

            if (_runtimeViewPanel.IsHidden)
                _runtimeViewPanel.Show();

            ActivateDockContentWithStepThrough(_runtimeViewPanel);
        }

        private void UpdateUI_ProjectName()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;

            var baseTitle = FlowBloxResourceUtil.GetLocalizedString("AppWindow_Text", typeof(FlowBloxMainUITexts));
            if (string.IsNullOrEmpty(project?.ProjectName))
            {
                Text = baseTitle;
                return;
            }

            // Build suffix:
            var suffixParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(project.ProjectSpaceGuid))
                suffixParts.Add($"Project-Space GUID: {project.ProjectSpaceGuid}");

            if (project.ProjectSpaceVersion.HasValue)
                suffixParts.Add($"Version: {project.ProjectSpaceVersion.Value}");

            var suffix = suffixParts.Count > 0
                ? $" ({string.Join(" ", suffixParts)})"
                : string.Empty;

            Text = $"{baseTitle} \"{project.ProjectName}\"{suffix}";
        }

        private async void itmNewProject_Click(object sender, EventArgs e)
        {
            bool createProject = true;

            if (FlowBloxProjectManager.Instance.ActiveProject != null)
            {
                DialogResult result = FlowBloxMessageBox.Show
                    (
                        this,
                        string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_NewProjectConfirm_Message", typeof(FlowBloxMainUITexts)), FlowBloxProjectManager.Instance.ActiveProject.ProjectName) + "\r\n" +
                        "\r\n" +
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveReminder_Message", typeof(FlowBloxMainUITexts)),
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_NewProjectConfirm_Title", typeof(FlowBloxMainUITexts)), FlowBloxMessageBox.Buttons.YesNo, FlowBloxMessageBox.Icons.Question
                    );

                if (result == DialogResult.No)
                {
                    createProject = false;
                }
            }

            if (createProject)
            {
                this.CloseProject();
                await this.CreateProjectAsync();
            }

            UpdateUI();
        }

        private void UnloadProject()
        {
            try
            {
                FlowBloxProjectManager.Instance.ActiveProject = null;
                FlowBloxProjectManager.Instance.ActiveProjectPath = null;
            }
            catch (ProjectExtensionsUnloadException ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();

                logger.Error("Failed to unload project due to remaining extensions.", ex);

                if (ex.RemainingAssemblies?.Any() == true)
                {
                    logger.Warn("Remaining Assemblies:");
                    foreach (var asm in ex.RemainingAssemblies)
                        logger.Warn($" - {asm}");
                }

                if (ex.UnloadableExtensions?.Any() == true)
                {
                    logger.Warn("Unloadable Extensions:");
                    foreach (var ext in ex.UnloadableExtensions)
                        logger.Warn($" - {ext}");
                }

                string title = FlowBloxResourceUtil.GetLocalizedString("AppWindow_UnloadProjectFailed_Title", typeof(FlowBloxMainUITexts));
                string message = string.Format(
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_UnloadProjectFailed_Message", typeof(FlowBloxMainUITexts)),
                    string.Join(Environment.NewLine, ex.RemainingAssemblies.Concat(ex.UnloadableExtensions)));

                FlowBloxMessageBox.Show(
                    this,
                    message,
                    title,
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Exclamation
                );

                string exePath = System.Windows.Forms.Application.ExecutablePath;
                using var _ = Process.Start(exePath);
                Environment.Exit(0);
            }
        }

        private async Task CreateProjectAsync()
        {
            var project = new FlowBloxProject();
            this._recentProjectPath = string.Empty;

            var projectView = new CreateEditProjectWindow(project);
            if (WindowsFormWPFHelper.ShowDialog(projectView, this) != true)
            {
                UnloadProject();
            }
            else
            {
                _isProjectLoading = true;
                ShowProjectLoadingOverlay(CreatingProjectOverlayText);
                UpdateUI();

                try
                {
                    FlowBloxProjectManager.Instance.ActiveProject = project;
                    FlowBloxProjectManager.Instance.ActiveProjectPath = _recentProjectPath;
                    this.AfterProjectFullyInitialized();
                    OnAfterProjectCreated();
                    UpdateUI_ProjectName();
                    await Task.Delay(AfterProjectActivationUiDelay);
                }
                finally
                {
                    _isProjectLoading = false;
                    HideProjectLoadingOverlay();
                    UpdateUI();
                }
            }
        }

        private void OnAfterProjectCreated()
        {
            this._dockContentProjectPanel.OnAfterProjectCreated();
            this.UpdateUI();
        }

        private void AfterProjectFullyInitialized(bool exceptAiAssistantView = false)
        {
            InitializeDockPanel(exceptAiAssistantView: exceptAiAssistantView);
            this._fieldViewPanel.AfterProjectFullyInitialized();
            this._managedObjectsViewPanel?.AfterProjectFullyInitialized();
            this._testViewPanel?.AfterProjectFullyInitialized();
            if (!exceptAiAssistantView)
                this._aiAssistantViewPanel?.AfterProjectFullyInitialized();
            this._dockContentProjectPanel.AfterProjectFullyInitialized();
        }

        private void CloseProject()
        {
            if (FlowBloxProjectManager.Instance.ActiveProject != null)
            {
                UnloadProject();
                this.itmDockablePanels.DropDownItems.Clear();
                this._dockContentProjectPanel?.OnAfterProjectClosed();
                UpdateUI();
                UpdateUI_ProjectName();
            }
        }

        private async Task OpenProjectWithConfirmationAsync(Func<Task> openProjectAction)
        {
            if (openProjectAction == null)
                throw new ArgumentNullException(nameof(openProjectAction));

            bool proceed = true;

            if (FlowBloxProjectManager.Instance.ActiveProject != null)
            {
                DialogResult result = FlowBloxMessageBox.Show(
                    this,
                    string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_OpenProjectConfirm_Message", typeof(FlowBloxMainUITexts)), FlowBloxProjectManager.Instance.ActiveProject.ProjectName) + "\r\n" +
                    "\r\n" +
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveReminder_Message", typeof(FlowBloxMainUITexts)),
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_OpenProjectConfirm_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.YesNo,
                    FlowBloxMessageBox.Icons.Question
                );

                if (result == DialogResult.No)
                    proceed = false;
            }

            if (proceed)
            {
                await openProjectAction();
            }
            else
            {
                UpdateUI();
            }
        }

        private void ShowProjectLoadingOverlay(string loadingText = LoadingProjectOverlayText)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(() => ShowProjectLoadingOverlay(loadingText)));
                return;
            }

            if (_waitOverlayWindow == null)
            {
                _waitOverlayWindow = new WaitOverlayWindow(loadingText);
                var helper = new System.Windows.Interop.WindowInteropHelper(_waitOverlayWindow)
                {
                    Owner = this.Handle
                };
            }

            PositionProjectLoadingOverlay();

            if (!_waitOverlayWindow.IsVisible)
                _waitOverlayWindow.Show();
        }

        private void PositionProjectLoadingOverlay()
        {
            if (_waitOverlayWindow == null)
                return;

            var clientAnchorPoint = new Point(this.ClientSize.Width / 2, (this.ClientSize.Height / 2) + 100);
            var screenAnchorPoint = this.PointToScreen(clientAnchorPoint);
            _waitOverlayWindow.Left = screenAnchorPoint.X - (_waitOverlayWindow.Width / 2d);
            _waitOverlayWindow.Top = screenAnchorPoint.Y - (_waitOverlayWindow.Height / 2d);
        }

        private void HideProjectLoadingOverlay()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new MethodInvoker(HideProjectLoadingOverlay));
                return;
            }

            if (_waitOverlayWindow == null)
                return;

            _waitOverlayWindow.Close();
            _waitOverlayWindow = null;
        }

        private string GetProjectDirectoryForDialogs()
        {
            var projectDirectory = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.PathsProjectDir].Value;
            if (!Directory.Exists(projectDirectory))
                Directory.CreateDirectory(projectDirectory);
            return projectDirectory;
        }

        private static string EnsureProjectFileExtension(string fileNameOrPath)
            => FlowBloxProjectFileNameHelper.EnsureExtension(fileNameOrPath);

        private string BuildDefaultProjectFileName()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            return FlowBloxProjectFileNameHelper.FromProjectName(project?.ProjectName);
        }

        private void ConfigureSaveProjectDialogDefaults()
        {
            saveProjectDialog.InitialDirectory = GetProjectDirectoryForDialogs();

            if (!string.IsNullOrWhiteSpace(_recentProjectPath))
            {
                saveProjectDialog.FileName = EnsureProjectFileExtension(Path.GetFileName(_recentProjectPath));
                return;
            }

            saveProjectDialog.FileName = BuildDefaultProjectFileName();
        }

        private async void rbOpenProject_Click(object sender, EventArgs e)
        {
            await OpenProjectWithConfirmationAsync(async () =>
            {
                openProjectDialog.InitialDirectory = GetProjectDirectoryForDialogs();

                if (openProjectDialog.ShowDialog(this) == DialogResult.OK)
                {
                    CloseProject();

                    _recentProjectPath = openProjectDialog.FileName;

                    UnloadProject();
                    await OpenProjectFromRecentProjectPathAsync();
                }
                else
                {
                    UpdateUI();
                }
            });
        }

        private void rbSaveProject_Click(object sender, EventArgs e)
        {
            if (IsActiveProjectFromDistributedData())
                return;

            try
            {
                if (string.IsNullOrWhiteSpace(_recentProjectPath))
                {
                    ConfigureSaveProjectDialogDefaults();

                    if (saveProjectDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        var newProjectPath = EnsureProjectFileExtension(saveProjectDialog.FileName);

                        SaveActiveProjectFile(newProjectPath);
                        _recentProjectPath = newProjectPath;
                    }
                }
                else
                {
                    SaveActiveProjectFile(_recentProjectPath);
                }
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);

                FlowBloxMessageBox.Show
                    (
                        this,
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveFailed_Message", typeof(FlowBloxMainUITexts)) + ex.Message,
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveFailed_Title", typeof(FlowBloxMainUITexts)),
                        FlowBloxMessageBox.Buttons.OK,
                        FlowBloxMessageBox.Icons.Error
                    );
            }

            UpdateUI();
        }

        private void rbSaveProjectAs_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_recentProjectPath))
                return;

            try
            {
                ConfigureSaveProjectDialogDefaults();

                if (saveProjectDialog.ShowDialog(this) == DialogResult.OK)
                {
                    var newProjectPath = EnsureProjectFileExtension(saveProjectDialog.FileName);

                    SaveActiveProjectFile(newProjectPath, generateNewProjectGuid: true);
                    _recentProjectPath = newProjectPath;
                }
            }
            catch (Exception Exception)
            {
                FlowBloxMessageBox.Show
                    (
                        this,
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveFailed_Message", typeof(FlowBloxMainUITexts)) + Exception.Message,
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveFailed_Title", typeof(FlowBloxMainUITexts)),
                        FlowBloxMessageBox.Buttons.OK,
                        FlowBloxMessageBox.Icons.Error
                    );
            }

            UpdateUI();
        }

        private void itmQuitApplication_Click(object sender, EventArgs e)
        {
            CloseApplicationControlled();
        }

        private void CloseApplicationControlled(string installerPathToLaunch = null)
        {
            _installerPathToLaunchOnClose = installerPathToLaunch;

            if (FlowBloxProjectManager.Instance.ActiveProject != null)
            {
                DialogResult result = FlowBloxMessageBox.Show
                    (
                        this,
                        string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveBeforeExit_Message", typeof(FlowBloxMainUITexts)), FlowBloxProjectManager.Instance.ActiveProject.ProjectName),
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveBeforeExit_Title", typeof(FlowBloxMainUITexts)),
                        FlowBloxMessageBox.Buttons.YesNo,
                        FlowBloxMessageBox.Icons.Question
                    );

                if (result == DialogResult.Yes)
                {
                    if (string.IsNullOrWhiteSpace(_recentProjectPath) || IsActiveProjectFromDistributedData())
                    {
                        ConfigureSaveProjectDialogDefaults();
                        if (saveProjectDialog.ShowDialog(this) == DialogResult.OK)
                        {
                        var newProjectPath = EnsureProjectFileExtension(saveProjectDialog.FileName);

                        SaveActiveProjectFile(newProjectPath);
                        _recentProjectPath = newProjectPath;
                        }
                    }
                    else
                    {
                        SaveActiveProjectFile(_recentProjectPath);
                    }
                }
            }

            this.Close();
        }

        internal void PrepareProjectForSave(FlowBloxProject project)
        {
            this._dockContentProjectPanel.PrepareProjectForSave(project);
        }

        internal void RestoreProjectAfterSaveAttempt(FlowBloxProject project)
        {
            this._dockContentProjectPanel.RestoreProjectAfterSaveAttempt(project);
        }

        private void SaveActiveProjectFile(string fileName, bool generateNewProjectGuid = false)
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            PrepareProjectForSave(project);
            try
            {
                project.Save(fileName, generateNewProjectGuid);
            }
            finally
            {
                RestoreProjectAfterSaveAttempt(project);
            }

            FlowBloxProjectManager.Instance.ActiveProjectPath = fileName;
        }

        private bool IsActiveProjectFromDistributedData() =>
            DistributedDataCatalog.IsDistributedDataPath(_recentProjectPath) ||
            DistributedDataCatalog.IsDistributedDataPath(FlowBloxProjectManager.Instance.ActiveProjectPath);

        private void ApplyExampleProjectMenuIcon()
        {
            try
            {
                var resourceUri = new Uri(
                    "/FlowBlox;component/Icons/presentation-play-custom.png",
                    UriKind.Relative);
                var resource = System.Windows.Application.GetResourceStream(resourceUri);
                if (resource == null)
                    return;

                using var resourceStream = resource.Stream;
                using var sourceImage = new Bitmap(resourceStream);
                itmOpenExampleProject.Image = new Bitmap(sourceImage);
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    "Failed to load the example-project menu icon.",
                    ex);
            }
        }

        private void itmOptions_Click(object sender, EventArgs e)
        {
            var optionsWindow = new UICore.Views.OptionsWindow();
            WindowsFormWPFHelper.ShowDialog(optionsWindow, this);
            UpdateUI();
        }

        private void itmUserFields_Click(object sender, EventArgs e)
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            UserFieldObjectManager userFieldObjectManager = new UserFieldObjectManager(registry);
            var propertyViewWpf = new UICore.Views.PropertyWindow(new PropertyWindowArgs(
                userFieldObjectManager,
                readOnly: IsProjectEditingReadOnly,
                deepCopy: false,
                canSave: false));
            propertyViewWpf.Height = 800;
            WindowsFormWPFHelper.ShowDialog(propertyViewWpf, this);
            UpdateUI();
        }

        private void itmCloseProject_Click(object sender, EventArgs e)
        {
            if (FlowBloxProjectManager.Instance.ActiveProject == null)
                return;

            DialogResult Result = FlowBloxMessageBox.Show
                (
                    this,
                    string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_CloseProjectConfirm_Message", typeof(FlowBloxMainUITexts)), FlowBloxProjectManager.Instance.ActiveProject.ProjectName) + "\r\n" +
                    "\r\n" +
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveReminder_Message", typeof(FlowBloxMainUITexts)),
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_CloseProjectConfirm_Title", typeof(FlowBloxMainUITexts)), FlowBloxMessageBox.Buttons.YesNo, FlowBloxMessageBox.Icons.Question
                );

            if (Result == DialogResult.Yes)
            {
                this.CloseProject();
            }

            UpdateUI();
        }

        private void itmRefresh_Click(object sender, EventArgs e)
        {
            UpdateUI();
        }

        private async Task OpenProjectFromRecentProjectPathAsync()
        {
            _recentProjectSpaceGuid = string.Empty;
            await TryOpenProjectAsync(() =>
            {
                var project = FlowBloxProject.FromFile(_recentProjectPath);
                FlowBloxProjectManager.Instance.ActiveProjectPath = _recentProjectPath;
                return project;
            });
        }

        private async Task<bool> TryOpenProjectAsync(Func<FlowBloxProject> projectLoader)
        {
            if (projectLoader == null)
                throw new ArgumentNullException(nameof(projectLoader));

            var openedSuccessfully = false;
            _isProjectLoading = true;
            ShowProjectLoadingOverlay();
            UpdateUI();

            FlowBloxProject project;
            try
            {
                project = await Task.Run(projectLoader);

                if (project == null)
                    return false;

                FlowBloxProjectManager.Instance.ActiveProject = project;

                this.AfterProjectFullyInitialized();
                this.OnAfterProjectOpened(project);
                openedSuccessfully = true;
            }
            catch (Exception e)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(e);

                FlowBloxMessageBox.Show(
                    this,
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_ProjectLoadFailed_Message", typeof(FlowBloxMainUITexts)),
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_ProjectLoadFailed_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Warning
                );

                FlowBloxMessageBox.Show(
                    this,
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_ErrorReport_Message", typeof(FlowBloxMainUITexts)) + "\r\n" +
                    "\r\n" +
                    "Exception:" + "\r\n" +
                    e.ToString(),
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_ErrorReport_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Info
                );

                return false;
            }
            finally
            {
                if (openedSuccessfully)
                    await Task.Delay(AfterProjectActivationUiDelay);
                _isProjectLoading = false;
                HideProjectLoadingOverlay();
                UpdateUI();
            }

            if (!string.IsNullOrEmpty(project.Notice))
            {
                FlowBloxMessageBox.Show(
                    this,
                    project.Notice,
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_ProjectNotice_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Info
                );
            }

            UpdateUI_ProjectName();
            return true;
        }

        private void OnAfterProjectOpened(FlowBloxProject project)
        {
            this._dockContentProjectPanel.OnAfterProjectOpened(project);
            RecentProjectsManager.Instance.RegisterOpenedProject(project, _recentProjectPath, project?.ProjectSpaceGuid);
            RefreshRecentProjectsMenu();
            this.UpdateUI();
        }

        internal async Task<bool> RestoreProjectStateWithoutConfirmationAsync(FlowBloxProject project)
        {
            if (project == null)
                return false;

            _isProjectLoading = true;
            UpdateUI();
            await Task.Delay(BeforeProjectRestoreUiDelay);
            ShowProjectLoadingOverlay(RestoringProjectStateOverlayText);

            try
            {
                UnloadProject();
                FlowBloxProjectManager.Instance.ActiveProjectPath = string.IsNullOrWhiteSpace(project.ProjectFilePath)
                    ? null
                    : project.ProjectFilePath;
                FlowBloxProjectManager.Instance.ActiveProject = project;

                AfterProjectFullyInitialized(exceptAiAssistantView: true);
                _dockContentProjectPanel?.OnAfterProjectOpened(project);

                UpdateUI_ProjectName();
                UpdateUI();
                await Task.Delay(AfterProjectActivationUiDelay);
                return true;
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);
                return false;
            }
            finally
            {
                _isProjectLoading = false;
                HideProjectLoadingOverlay();
                UpdateUI();
            }
        }

        private void AppWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_dockContentProjectPanel?.IsRuntimeActive == true)
            {
                DialogResult Result = FlowBloxMessageBox.Show
                    (
                        this,
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_RuntimeActiveClose_Message", typeof(FlowBloxMainUITexts)),
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_RuntimeActiveClose_Title", typeof(FlowBloxMainUITexts)),
                        FlowBloxMessageBox.Buttons.OK,
                        FlowBloxMessageBox.Icons.Info
                );

                e.Cancel = true;
                _installerPathToLaunchOnClose = null;
            }
            else
            {
                try
                {
                    _appNotificationService.NotificationsChanged -= AppNotificationService_NotificationsChanged;

                    if (!string.IsNullOrWhiteSpace(_installerPathToLaunchOnClose))
                    {
                        if (!LaunchInstaller(_installerPathToLaunchOnClose))
                            FlowBloxLogManager.Instance.GetLogger().Error("Failed to launch installer during controlled close.");

                        _installerPathToLaunchOnClose = null;
                    }

                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error("Failed to terminate process on AppWindow close.", ex);
                }
            }
        }

        private void itmToolbox_Click(object sender, EventArgs e)
        {
            var dialog = new ToolboxWindow();
            WindowsFormWPFHelper.ShowDialog(dialog, this.FindForm());
        }

        private void itmEditProject_Click(object sender, EventArgs e)
        {
            if (FlowBloxProjectManager.Instance.ActiveProject == null)
                return;

            var projectView = new CreateEditProjectWindow(FlowBloxProjectManager.Instance.ActiveProject, isEditing: true);
            WindowsFormWPFHelper.ShowDialog(projectView, this);
            UpdateUI();
            UpdateUI_ProjectName();
        }

        private async void AppWindow_Load(object sender, EventArgs e)
        {
            lblApplicationVersion.Visible = false;
            RefreshNotificationBar();
            RefreshRecentProjectsMenu();
            await InitPreconfiguredProjectAsync();
            await InitializeNotificationsAsync();
        }

        public void SetProjectFile(string projectFile) => _recentProjectPath = projectFile;

        public void SetProjectSpaceGuid(string projectSpaceGuid) => _recentProjectSpaceGuid = projectSpaceGuid;

        private async Task InitPreconfiguredProjectAsync()
        {
            if (File.Exists(_recentProjectPath))
                await OpenProjectFromRecentProjectPathAsync();

            if (!string.IsNullOrEmpty(_recentProjectSpaceGuid))
                await OpenProjectFromProjectSpaceAsync(_recentProjectSpaceGuid);
        }

        private void itmOpenOutputDir_Click(object sender, EventArgs e)
        {
            string outputDirectory = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.PathsOutputDir].Value;
            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            Process.Start(new ProcessStartInfo()
            {
                FileName = "explorer.exe",
                Arguments = outputDirectory,
                UseShellExecute = true
            });
        }

        private void itmAbout_Click(object sender, EventArgs e)
        {
            var aboutWindow = new AboutWindow();
            WindowsFormWPFHelper.ShowDialog(aboutWindow, this);
            UpdateUI();
        }

        private void itmLicense_Click(object sender, EventArgs e)
        {

        }

        private void itmSaveGridImage_Click(object sender, EventArgs e)
        {
            try
            {
                string OutputDirectory = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.PathsOutputDir].Value;
                saveFileDialog_GridImage.InitialDirectory = OutputDirectory;
                if (!Directory.Exists(OutputDirectory))
                {
                    Directory.CreateDirectory(OutputDirectory);
                }
                if (saveFileDialog_GridImage.ShowDialog(this) == DialogResult.OK)
                    _dockContentProjectPanel.SaveInnerPanelBitmap(saveFileDialog_GridImage.FileName);
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);

                FlowBloxMessageBox.Show
                    (
                        this,
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_GridExportFailed_Message", typeof(FlowBloxMainUITexts)),
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_GridExportFailed_Title", typeof(FlowBloxMainUITexts)),
                        FlowBloxMessageBox.Buttons.OK,
                        FlowBloxMessageBox.Icons.Warning
                    );
            }

            UpdateUI();
        }

        private void View_FormClosed(object sender, FormClosedEventArgs e)
        {
            UpdateUI();
        }

        private void itmUndo_Click(object sender, EventArgs e)
        {
            _dockContentProjectPanel.Undo();
        }

        private void itmRedo_Click(object sender, EventArgs e)
        {
            _dockContentProjectPanel.Redo();
        }

        private void itmOpenApplicationLogDirectory_Click(object sender, EventArgs e)
        {
            var logger = FlowBloxLogManager.Instance.GetLogger();
            var logPath = logger.GetLogfilePath();

            if (File.Exists(logPath))
            {
                var logDirectory = Path.GetDirectoryName(logPath);
                Process.Start("explorer.exe", logDirectory);
            }
        }

        private void itmOpenRuntimeLogDirectory_Click(object sender, EventArgs e)
        {
            var logger = FlowBloxLogManager.Instance.GetLogger();
            var logPath = logger.GetLogfilePath();

            if (File.Exists(logPath))
            {
                var logDirectory = Path.GetDirectoryName(logPath);
                Process.Start("explorer.exe", logDirectory);
            }
        }

        private void itmOpenProjectDir_Click(object sender, EventArgs e)
        {
            string projectDirectory = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.PathsProjectDir].Value;
            if (!Directory.Exists(projectDirectory))
                Directory.CreateDirectory(projectDirectory);

            Process.Start(new ProcessStartInfo()
            {
                FileName = "explorer.exe",
                Arguments = projectDirectory,
                UseShellExecute = true
            });
        }

        private void Runtime_LogMessageCreated(BaseRuntime runtime, string message, FlowBloxLogLevel logLevel)
        {
            _runtimeViewPanel.Append(message, logLevel);
        }

        private void Runtime_ProblemTraceCreated(BaseRuntime runtime, ProblemTrace problemTrace)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new ProblemTraceCreatedEventHandler(Runtime_ProblemTraceCreated), new object[2] { runtime, problemTrace });
                return;
            }

            _problemsViewPanel.Append(problemTrace);
        }

        internal void OnBeforeRuntimeStarted(BaseRuntime runtime)
        {
            runtime.LogMessageCreated += Runtime_LogMessageCreated;

            foreach (var backtraceInterceptor in runtime.Interceptors.OfType<RuntimeBacktraceInterceptor>())
            {
                backtraceInterceptor.ProblemTraceCreated += Runtime_ProblemTraceCreated;
            }

            _runtimeViewPanel.InitializeRuntime(runtime);

            UpdateUI();

            _runtimeViewPanel.Focus();
        }

        internal void OnAfterRuntimeFinished()
        {
            UpdateUI();

        }

        private void itmPaste_Click(object sender, EventArgs e)
        {
            ProjectPanelWpfControl?.PasteSelection();
        }

        private void itmCopy_Click(object sender, EventArgs e)
        {
            CopyProjectPanelWpfSelection();
        }

        private void CopyProjectPanelWpfSelection()
        {
            var projectPanelWpfControl = ProjectPanelWpfControl;
            if (projectPanelWpfControl == null)
                return;

            if (projectPanelWpfControl.SelectedNodeCount > 1)
            {
                var messageBoxService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();
                messageBoxService?.ShowMessageBox(
                    FlowBloxResourceUtil.GetLocalizedString("ProjectPanel_MultiPasteNotSupported_Message", typeof(FlowBloxMainUITexts)),
                    FlowBloxResourceUtil.GetLocalizedString("ProjectPanel_MultiPasteNotSupported_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBoxTypes.Information);
            }

            projectPanelWpfControl.CopySelection();
        }


        private void itmVisitOnline_Click(object sender, EventArgs e) => OpenUrl(GlobalUrls.FlowBloxWebsite);

        private void itmGitHub_Click(object sender, EventArgs e) => OpenUrl(GlobalUrls.FlowBloxGitHubRepository);

        private async void itmCheckForNewVersion_Click(object sender, EventArgs e)
        {
            await CheckForUpdatesAsync(showMessageWhenNoUpdate: true);
        }

        private async Task InitializeNotificationsAsync()
        {
            _appNotificationService.RemoveExpiredNotifications();
            await CheckForUpdatesAsync(showMessageWhenNoUpdate: false);
        }

        private async Task CheckForUpdatesAsync(bool showMessageWhenNoUpdate)
        {
            try
            {
                var manifestUrl = FlowBloxOptions.GetOptionInstance()
                    .GetOption(OptionsConstants.UpdatesInstallerManifestUrl)
                    ?.Value;

                if (string.IsNullOrWhiteSpace(manifestUrl))
                {
                    if (showMessageWhenNoUpdate)
                    {
                        PublishUpdateStatusNotification(
                            FlowBloxResourceUtil.GetLocalizedString("AppWindow_UpdateCheck_NotPackaged_Message", typeof(FlowBloxMainUITexts)),
                            DefaultNotificationLifetime);
                    }

                    return;
                }

                var latestUpdate = await _flowBloxInstallerService.GetLatestInstallerUpdateAsync(manifestUrl);
                if (latestUpdate == null)
                {
                    if (showMessageWhenNoUpdate)
                    {
                        PublishUpdateStatusNotification(
                            FlowBloxResourceUtil.GetLocalizedString("AppWindow_UpdateCheck_Error_Message", typeof(FlowBloxMainUITexts)),
                            DefaultNotificationLifetime);
                    }

                    return;
                }

                var currentVersion = FlowBloxVersionHelper.ParseComparableVersion(Application.ProductVersion);
                if (latestUpdate.Version > currentVersion)
                {
                    _appNotificationService.Publish(new AppNotification
                    {
                        Id = UpdateNotificationId,
                        Message = FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notification_UpdateAvailable_Message", typeof(FlowBloxMainUITexts)),
                        ActionText = FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notification_UpdateAvailable_Action_Text", typeof(FlowBloxMainUITexts)),
                        ActionType = AppNotificationActionType.DownloadUpdateInstaller,
                        ActionData = latestUpdate.InstallerUrl,
                        ExpiresUtc = DateTimeOffset.UtcNow.Add(DefaultNotificationLifetime)
                    });
                }
                else
                {
                    _appNotificationService.Dismiss(UpdateNotificationId);
                    if (showMessageWhenNoUpdate)
                        PublishUpdateStatusNotification(FlowBloxResourceUtil.GetLocalizedString("AppWindow_UpdateCheck_NoUpdates_Message", typeof(FlowBloxMainUITexts)));
                }
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);
                PublishUpdateStatusNotification(
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_UpdateCheck_Exception_Message", typeof(FlowBloxMainUITexts)),
                    DefaultNotificationLifetime);
            }
        }

        private bool LaunchInstaller(string installerPath)
        {
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
                return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to start installer.", ex);
                return false;
            }
        }

        private void PublishUpdateStatusNotification(string message, TimeSpan? lifetime = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _appNotificationService.Publish(new AppNotification
            {
                Id = UpdateStatusNotificationId,
                Message = message,
                ActionText = string.Empty,
                ActionType = AppNotificationActionType.None,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultNotificationLifetime)
            });
        }

        private void AppNotificationService_NotificationsChanged(object sender, EventArgs e)
        {
            RefreshNotificationBar();
        }

        private void RefreshNotificationBar()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(RefreshNotificationBar));
                return;
            }

            _appNotificationService.RemoveExpiredNotifications();

            var notification = _appNotificationService.GetCurrentNotification();
            if (notification == null)
            {
                lblNotificationMessage.Text = string.Empty;
                lblNotificationAction.Text = string.Empty;
                lblNotificationAction.Visible = false;
                statusStrip.Visible = false;
                return;
            }

            lblNotificationMessage.Text = notification.Message ?? string.Empty;
            lblNotificationAction.Text = notification.ActionText;
            lblNotificationAction.Visible = !string.IsNullOrWhiteSpace(notification.ActionText)
                                            && notification.ActionType != AppNotificationActionType.None;
            statusStrip.Visible = true;
        }

        private async void lblNotificationAction_Click(object sender, EventArgs e)
        {
            if (_isNotificationActionRunning)
                return;

            var notification = _appNotificationService.GetCurrentNotification();
            if (notification == null)
                return;

            try
            {
                _isNotificationActionRunning = true;

                switch (notification.ActionType)
                {
                    case AppNotificationActionType.DownloadUpdateInstaller:
                        var installerPath = await _flowBloxInstallerService.DownloadInstallerAsync(notification.ActionData, UpdateDownloadDirectoryName);
                        if (string.IsNullOrWhiteSpace(installerPath))
                        {
                            PublishUpdateStatusNotification(
                                FlowBloxResourceUtil.GetLocalizedString("AppWindow_UpdateInstall_Failed_Message", typeof(FlowBloxMainUITexts)),
                                DefaultNotificationLifetime);
                            break;
                        }

                        _downloadedInstallerPath = installerPath;
                        _appNotificationService.Publish(new AppNotification
                        {
                            Id = UpdateNotificationId,
                            Message = FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notification_UpdateDownloaded_Message", typeof(FlowBloxMainUITexts)),
                            ActionText = FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notification_UpdateInstallDownloaded_Action_Text", typeof(FlowBloxMainUITexts)),
                            ActionType = AppNotificationActionType.InstallDownloadedUpdate,
                            ActionData = installerPath,
                            ExpiresUtc = DateTimeOffset.UtcNow.Add(DefaultNotificationLifetime)
                        });
                        break;
                    case AppNotificationActionType.InstallDownloadedUpdate:
                        var pathToInstall = notification.ActionData;
                        if (string.IsNullOrWhiteSpace(pathToInstall))
                            pathToInstall = _downloadedInstallerPath;

                        if (!string.IsNullOrWhiteSpace(pathToInstall))
                        {
                            _appNotificationService.Dismiss(notification.Id);
                            CloseApplicationControlled(pathToInstall);
                        }
                        break;
                    case AppNotificationActionType.None:
                    default:
                        break;
                }
            }
            finally
            {
                _isNotificationActionRunning = false;
            }
        }

        private void itmReportProblem_Click(object sender, EventArgs e) => OpenUrl(GlobalUrls.FlowBloxReportProblem);

        /// <summary>
        /// Opens a URL in the default browser.
        /// </summary>
        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);

                FlowBloxMessageBox.Show
                (
                    this,
                    string.Format(
                        FlowBloxResourceUtil.GetLocalizedString(
                            "AppWindow_OpenUrlFailed_Message",
                            typeof(FlowBloxMainUITexts)),
                        url),
                    FlowBloxResourceUtil.GetLocalizedString(
                        "AppWindow_OpenUrlFailed_Title",
                        typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Warning
                );
            }
        }

        private void itmResetDockablePanels_Click(object sender, EventArgs e)
        {
            FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.MainPanelDockSettings).Value = string.Empty;
            FlowBloxOptions.GetOptionInstance().Save();
            itmDockablePanels.DropDownItems.Clear();
            InitializeDockPanel(true);
        }

        private void itmDockablePanels_Click(object sender, EventArgs e)
        {
        }

        private void itmSaveToProjectSpace_Click(object sender, EventArgs e)
        {
            if (IsActiveProjectFromDistributedData())
                return;

            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var dialog = new CreateOrUpdatePSProjectWindow(
                project,
                PrepareProjectForSave,
                RestoreProjectAfterSaveAttempt);
            WindowsFormWPFHelper.ShowDialog(dialog, this);
        }

        private async void itmOpenFromProjectSpace_Click(object sender, EventArgs e)
        {
            await OpenProjectWithConfirmationAsync(async () =>
            {
                var dialog = new PSProjectsWindow();
                WindowsFormWPFHelper.ShowDialog(dialog, this);

                if (dialog.DialogResult != true)
                    return;

                var selection = dialog.Tag as PSProjectSelection;
                if (selection?.Project == null || string.IsNullOrWhiteSpace(selection.Project.Guid))
                    return;

                var projectGuid = selection.Project.Guid;
                int? version = selection.Version?.VersionNumber;

                CloseProject();
                UnloadProject();
                await OpenProjectFromProjectSpaceAsync(projectGuid, version);
            });
        }

        private async void itmOpenExampleProject_Click(object sender, EventArgs e)
        {
            await OpenProjectWithConfirmationAsync(async () =>
            {
                var dialog = new ExampleProjectsWindow();
                WindowsFormWPFHelper.ShowDialog(dialog, this);
                if (dialog.DialogResult != true || dialog.Tag is not string projectFile)
                    return;

                CloseProject();
                _recentProjectPath = projectFile;
                await OpenProjectFromRecentProjectPathAsync();
            });
        }

        private async Task OpenProjectFromProjectSpaceAsync(string projectGuid, int? projectSpaceVersion = null)
        {
            _recentProjectPath = string.Empty;
            _recentProjectSpaceGuid = projectGuid;

            var baseUrl = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.ApiProjectServiceBaseUrl].Value;
            var webApi = new FlowBloxWebApiService(baseUrl);
            var token = FlowBloxAccountManager.Instance.GetUserToken(baseUrl);

            FlowBloxProject loadedProject = null;
            await TryOpenProjectAsync(() =>
            {
                loadedProject = Task.Run(async () =>
                        await FlowBloxProject.FromProjectSpaceGuidAsync(projectGuid, projectSpaceVersion, token, webApi))
                    .GetAwaiter()
                    .GetResult();

                FlowBloxProjectManager.Instance.ActiveProjectPath = null;

                return loadedProject;
            });
        }

        private async void itmFbProjects_Click(object sender, EventArgs e)
        {
            var dialog = new PSProjectsWindow();
            WindowsFormWPFHelper.ShowDialog(dialog, this);
            if (dialog.DialogResult != true)
                return;

            var selection = dialog.Tag as PSProjectSelection;
            if (selection?.Project == null || string.IsNullOrWhiteSpace(selection.Project.Guid))
                return;

            var projectGuid = selection.Project.Guid;
            int? version = selection.Version?.VersionNumber;

            await OpenProjectWithConfirmationAsync(async () =>
            {
                CloseProject();
                UnloadProject();
                await OpenProjectFromProjectSpaceAsync(projectGuid, version);
            });
        }

        private void itmFbExtensions_Click(object sender, EventArgs e)
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var dialog = project == null
                ? new ExtensionsWindow()
                : new ExtensionsWindow(
                    project,
                    PrepareProjectForSave,
                    RestoreProjectAfterSaveAttempt);
            WindowsFormWPFHelper.ShowDialog(dialog, this);
        }

        private void itmFlowBloxTaskManagement_Click(object sender, EventArgs e)
        {
            var dialog = new FlowBloxTaskManagementWindow();
            WindowsFormWPFHelper.ShowDialog(dialog, this);
        }

        private void itmNotifications_Click(object sender, EventArgs e)
        {
            FlowBloxOptions.GetOptionInstance().InitDefaults(false);
            var configuration = RuntimeNotificationConfigurationStore.Load(out var loadError);
            if (!string.IsNullOrWhiteSpace(loadError))
            {
                FlowBloxMessageBox.Show(
                    this,
                    loadError,
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notifications_LoadError_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Warning);
                return;
            }

            var propertyWindow = new PropertyWindow(new PropertyWindowArgs(
                configuration,
                readOnly: false,
                canSave: true,
                detached: true))
            {
                Height = 760,
                Width = 860
            };

            WindowsFormWPFHelper.ShowDialog(propertyWindow, this);
            if (propertyWindow.DialogResult == true &&
                !RuntimeNotificationConfigurationStore.Save(configuration, out var saveError))
            {
                FlowBloxMessageBox.Show(
                    this,
                    saveError,
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notifications_SaveError_Title", typeof(FlowBloxMainUITexts)),
                    FlowBloxMessageBox.Buttons.OK,
                    FlowBloxMessageBox.Icons.Error);
            }
        }

        private void itmManageInputFiles_Click(object sender, EventArgs e)
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            if (project == null)
                return;

            var dialog = new ManageInputFilesWindow(project, readOnly: IsProjectEditingReadOnly);
            WindowsFormWPFHelper.ShowDialog(dialog, this.FindForm());
        }

        private void itmOpenInputDir_Click(object sender, EventArgs e)
        {
            string inputDirectory = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.PathsInputDir].Value;
            if (!Directory.Exists(inputDirectory))
                Directory.CreateDirectory(inputDirectory);

            Process.Start(new ProcessStartInfo()
            {
                FileName = "explorer.exe",
                Arguments = inputDirectory,
                UseShellExecute = true
            });
        }

        private void itmOpenProjectInputDir_Click(object sender, EventArgs e)
        {
            var projectInputDirectory = FlowBloxProjectManager.Instance.ActiveProject?.ProjectInputDirectory;
            if (string.IsNullOrWhiteSpace(projectInputDirectory))
                return;

            if (!Directory.Exists(projectInputDirectory))
                Directory.CreateDirectory(projectInputDirectory);

            Process.Start(new ProcessStartInfo()
            {
                FileName = "explorer.exe",
                Arguments = projectInputDirectory,
                UseShellExecute = true
            });
        }

        private void itmOpenProjectOutputDir_Click(object sender, EventArgs e)
        {
            var projectOutputDirectory = FlowBloxProjectManager.Instance.ActiveProject?.ProjectOutputDirectory;
            if (string.IsNullOrWhiteSpace(projectOutputDirectory))
                return;

            if (!Directory.Exists(projectOutputDirectory))
                Directory.CreateDirectory(projectOutputDirectory);

            Process.Start(new ProcessStartInfo()
            {
                FileName = "explorer.exe",
                Arguments = projectOutputDirectory,
                UseShellExecute = true
            });
        }

        private void itmOpenOnnxQaModelRootDirectory_Click(object sender, EventArgs e)
        {
            OpenOptionDirectory(OptionsConstants.AIOnnxQAModelRootDirectory);
        }

        private void itmOpenOnnxGenAiModelRootDirectory_Click(object sender, EventArgs e)
        {
            OpenOptionDirectory(OptionsConstants.AIOnnxGenAIModelRootDirectory);
        }

        private static void OpenOptionDirectory(string optionName)
        {
            var directory = FlowBloxOptions.GetOptionInstance().GetOption(optionName)?.Value;
            if (string.IsNullOrWhiteSpace(directory))
                return;

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = directory,
                UseShellExecute = true
            });
        }

        private void RefreshRecentProjectsMenu()
        {
            itmRecentProjects.DropDownItems.Clear();

            var recentProjects = RecentProjectsManager.Instance.GetRecentProjects();
            foreach (var recentProject in recentProjects)
            {
                var item = new ToolStripMenuItem
                {
                    Text = BuildRecentProjectDisplayText(recentProject),
                    Tag = recentProject
                };
                item.Click += itmRecentProjectsItem_Click;
                itmRecentProjects.DropDownItems.Add(item);
            }

            FlowBloxStyle.ApplyStyle(menuStrip);
        }

        private static string BuildRecentProjectDisplayText(RecentProjectEntry entry)
        {
            if (entry == null)
                return string.Empty;

            var descriptor = string.Empty;
            if (!string.IsNullOrWhiteSpace(entry.ProjectFilePath))
                descriptor = Path.GetFileNameWithoutExtension(entry.ProjectFilePath);
            else if (!string.IsNullOrWhiteSpace(entry.ProjectSpaceGuid))
                descriptor = entry.ProjectSpaceGuid;

            if (string.IsNullOrWhiteSpace(entry.ProjectName))
                return descriptor;

            if (string.IsNullOrWhiteSpace(descriptor) ||
                string.Equals(entry.ProjectName, descriptor, StringComparison.OrdinalIgnoreCase))
            {
                return entry.ProjectName;
            }

            return $"{entry.ProjectName} ({descriptor})";
        }

        private async void itmRecentProjectsItem_Click(object sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem menuItem)
                return;

            if (menuItem.Tag is not RecentProjectEntry recentProject)
                return;

            await OpenProjectWithConfirmationAsync(async () =>
            {
                if (!string.IsNullOrWhiteSpace(recentProject.ProjectFilePath))
                {
                    if (!File.Exists(recentProject.ProjectFilePath))
                    {
                        RecentProjectsManager.Instance.RemoveEntry(recentProject);
                        RefreshRecentProjectsMenu();
                        UpdateUI();
                        return;
                    }

                    CloseProject();
                    _recentProjectPath = recentProject.ProjectFilePath;
                    _recentProjectSpaceGuid = string.Empty;
                    UnloadProject();
                    await OpenProjectFromRecentProjectPathAsync();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(recentProject.ProjectSpaceGuid))
                {
                    CloseProject();
                    _recentProjectPath = string.Empty;
                    _recentProjectSpaceGuid = recentProject.ProjectSpaceGuid;
                    UnloadProject();
                    await OpenProjectFromProjectSpaceAsync(recentProject.ProjectSpaceGuid);
                }
            });
        }

        internal T GetAccessibleComponent<T>() where T : System.Windows.Forms.Control
        {
            if (typeof(T) == typeof(ProjectPanel))
                return (T)(object)_dockContentProjectPanel;

            return default(T);
        }
    }
}
