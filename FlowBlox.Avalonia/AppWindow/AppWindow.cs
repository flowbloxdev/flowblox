using FlowBlox.AvaloniaDesktop.Resources;
using FlowBlox.AppWindow.Contents;
using FlowBlox.AvaloniaDesktop.Interop;
using FlowBlox.AvaloniaDesktop.Persistence.RecentProjects;
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
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.UICore.Models;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Factory;
using FlowBlox.UICore.ViewModels.PSProjects;
using FlowBlox.UICore.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static FlowBlox.Core.Interceptors.RuntimeBacktraceInterceptor;
using FlowBlox.AvaloniaDesktop.Providers;
using FlowBlox.AvaloniaDesktop.ViewModels;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.Core.Services.Notifications;
using Key = Avalonia.Input.Key;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace FlowBlox.AppWindow
{
    public partial class AppWindow : DesktopWindow
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

        private bool _isProjectLoading;
        private bool _isProjectUnloading;

        private readonly IAppNotificationService _appNotificationService;
        private readonly IFlowBloxInstallerService _flowBloxInstallerService;
        private readonly IRuntimeStateService _runtimeStateService;
        private bool _isNotificationActionRunning;
        private string _downloadedInstallerPath;
        private string _installerPathToLaunchOnClose;


        private FlowBloxProjectComponentProvider _componentProvider;
        private ProjectChangelist _subscribedChangelist;
        private FlowBloxProject _subscribedProjectForGuidChanges;

        public AppWindow()
        {
            InitializeComponent();




            _componentProvider = FlowBloxServiceLocator.Instance.GetService<FlowBloxProjectComponentProvider>();
            _appNotificationService = FlowBloxServiceLocator.Instance.GetService<IAppNotificationService>();
            _flowBloxInstallerService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxInstallerService>();
            _runtimeStateService = FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>();
            _runtimeStateService.StateChanged += RuntimeStateChanged;
            _appNotificationService.NotificationsChanged += AppNotificationService_NotificationsChanged;


            FlowBloxProjectManager.Instance.ProjectChanged += ProjectManager_ProjectChanged;

            RefreshRecentProjectsMenu();
            SubscribeCurrentChangelist();
            SubscribeCurrentProjectGuidChanges();
            this.UpdateUI();

        }

        protected override void OnClosed(EventArgs e)
        {
            FlowBloxProjectManager.Instance.ProjectChanged -= ProjectManager_ProjectChanged;
            UnsubscribeCurrentChangelist();
            UnsubscribeCurrentProjectGuidChanges();
            _runtimeStateService.StateChanged -= RuntimeStateChanged;
            _factory?.DetachContent(Workspace.Layout);
            DisposePanels();
            base.OnClosed(e);
        }

        private bool ProcessHostedShortcut(Key key, KeyModifiers modifiers)
        {
            if (ProcessProjectPanelWpfCmdKey(key, modifiers)) return true;
            foreach (var entry in AllMenuEntries())
                if (entry.Enabled && entry.Gesture is { } gesture && gesture.Key == key && gesture.KeyModifiers == modifiers)
                { entry.Execute(null); return true; }
            return false;
        }

        private ProjectPanelWpfControl ProjectPanelWpfControl => _dockContentProjectPanel?.WpfControl;

        private bool ProcessProjectPanelWpfCmdKey(Key key, KeyModifiers modifiers)
        {
            var panel = ProjectPanelWpfControl;
            if (panel == null || panel.IsTextInputFocusActive) return false;
            if (_dockContentProjectPanel?.HasWpfKeyboardFocus != true && !panel.CanHandleHostedShortcut) return false;
            if (modifiers == (KeyModifiers.Control | KeyModifiers.Shift) &&
                key is Key.LeftCtrl or Key.RightCtrl
                    or Key.LeftShift or Key.RightShift)
            {
                panel.ActivateTemporaryConnectionShortcut();
                return true;
            }
            if (modifiers == KeyModifiers.None)
                return key switch
                {
                    Key.Escape => panel.ExecuteEscapeShortcut(),
                    Key.Delete => panel.ExecuteDeleteShortcut(),
                    _ => false
                };
            if (modifiers != KeyModifiers.Control) return false;
            switch (key)
            {
                case Key.C: CopyProjectPanelWpfSelection(); return true;
                case Key.V: panel.PasteSelection(); return true;
                case Key.Left: return panel.ExecuteSelectionShortcut(System.Windows.Input.Key.Left);
                case Key.Right: return panel.ExecuteSelectionShortcut(System.Windows.Input.Key.Right);
                case Key.Up: return panel.ExecuteSelectionShortcut(System.Windows.Input.Key.Up);
                case Key.Down: return panel.ExecuteSelectionShortcut(System.Windows.Input.Key.Down);
                case Key.A: return panel.ExecuteSelectionShortcut(System.Windows.Input.Key.A);
                default: return false;
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
                this.Invoke(new Action(UpdateUI));
                return;
            }

            var project = FlowBloxProjectManager.Instance.ActiveProject;
            var isRuntimeActive = IsProjectEditingReadOnly;

            var isProjectActive = project != null;

            UpdateWorkspaceVisibility();


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

            if (IsRuntimeActive)
                BeginInvoke(new Action(ActivateRuntimeViewPanel));
        }

        private void ActivateRuntimeViewPanel() { if (_runtimeViewPanel != null) ActivatePanel(_runtimeViewPanel); }

        internal void UpdateUI_ProjectName()
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
                MessageBoxResult result = await DesktopDialogs.Messages.ShowAsync(string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_NewProjectConfirm_Message", typeof(FlowBloxMainUITexts)), FlowBloxProjectManager.Instance.ActiveProject.ProjectName) + "\r\n" +
                        "\r\n" +
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveReminder_Message", typeof(FlowBloxMainUITexts)),
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_NewProjectConfirm_Title", typeof(FlowBloxMainUITexts)), MessageBoxButtons.YesNo, MessageBoxIcon.Question
                    );

                if (result == MessageBoxResult.No)
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
            _isProjectUnloading = true;
            try
            {
                // Hide native WPF hosts before teardown can refresh their contents or pump UI messages.
                UpdateWorkspaceVisibility();
                UpdateLayout();
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

                DesktopDialogs.Messages.Show(message,
                    title,
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Warning
                );

                string exePath = Environment.ProcessPath;
                using var _ = Process.Start(exePath);
                Environment.Exit(0);
            }
            finally
            {
                _isProjectUnloading = false;
                UpdateWorkspaceVisibility();
            }
        }

        private void UpdateWorkspaceVisibility()
        {
            var wasWorkspaceVisible = Workspace.IsVisible;
            Workspace.IsVisible = FlowBloxProjectManager.Instance.ActiveProject != null
                && !_isProjectLoading
                && !_isProjectUnloading;
            Welcome.IsVisible = !Workspace.IsVisible;

            if (wasWorkspaceVisible != Workspace.IsVisible)
            {
                // NativeControlHost normally applies ancestor visibility after rendering.
                // Hide its HWNDs before project teardown can clear their WPF content.
                if (!Workspace.IsVisible)
                {
                    foreach (var panel in Panels())
                        panel.TryUpdateNativeControlPosition();
                }

                // Repaint the shell as well as the former native child-window regions.
                InvalidateVisual();
                FlowBlox.AvaloniaDesktop.Interop.Windows.WindowRepaint.Request(Handle);
            }
        }

        private async Task CreateProjectAsync()
        {
            var project = new FlowBloxProject();
            this._recentProjectPath = string.Empty;

            var projectView = new CreateEditProjectWindow(project);
            if (WpfDialogHost.ShowDialog(projectView, this) != true)
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

        internal void AfterProjectFullyInitialized(bool exceptAiAssistantView = false)
        {
            InitializeDockPanel(exceptAiAssistantView: exceptAiAssistantView);
            this._fieldViewPanel.AfterProjectFullyInitialized();
            this._managedObjectsViewPanel?.AfterProjectFullyInitialized();
            this._testViewPanel?.AfterProjectFullyInitialized();
            if (!exceptAiAssistantView)
                this._aiAssistantViewPanel?.AfterProjectFullyInitialized();
            this._dockContentProjectPanel.AfterProjectFullyInitialized();
        }

        internal void CloseProject()
        {
            if (FlowBloxProjectManager.Instance.ActiveProject != null)
            {
                UnloadProject();
    
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
                MessageBoxResult result = await DesktopDialogs.Messages.ShowAsync(string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_OpenProjectConfirm_Message", typeof(FlowBloxMainUITexts)), FlowBloxProjectManager.Instance.ActiveProject.ProjectName) + "\r\n" +
                    "\r\n" +
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveReminder_Message", typeof(FlowBloxMainUITexts)),
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_OpenProjectConfirm_Title", typeof(FlowBloxMainUITexts)),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == MessageBoxResult.No)
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

        private void ShowProjectLoadingOverlay(string loadingText = LoadingProjectOverlayText) { LoadingText.Text = loadingText; LoadingOverlay.IsVisible = true; Workspace.IsVisible = false; }

        private void PositionProjectLoadingOverlay() { }

        private void HideProjectLoadingOverlay() { LoadingOverlay.IsVisible = false; }

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

        private Task<string> PickSaveProjectPathAsync() => DesktopDialogs.Files.SaveProjectAsync(
            GetProjectDirectoryForDialogs(),
            !string.IsNullOrWhiteSpace(_recentProjectPath)
                ? EnsureProjectFileExtension(Path.GetFileName(_recentProjectPath))
                : BuildDefaultProjectFileName());

        private async void rbOpenProject_Click(object sender, EventArgs e)
        {
            await OpenProjectWithConfirmationAsync(async () =>
            {
                var selectedPath = await DesktopDialogs.Files.OpenProjectAsync(GetProjectDirectoryForDialogs());
                if (selectedPath != null)
                {
                    CloseProject();

                    _recentProjectPath = selectedPath;

                    UnloadProject();
                    await OpenProjectFromRecentProjectPathAsync();
                }
                else
                {
                    UpdateUI();
                }
            });
        }

        private async void rbSaveProject_Click(object sender, EventArgs e)
        {
            if (IsActiveProjectFromDistributedData())
                return;

            try
            {
                if (string.IsNullOrWhiteSpace(_recentProjectPath))
                {
                    var selectedPath = await PickSaveProjectPathAsync();
                    if (selectedPath != null)
                    {
                        var newProjectPath = EnsureProjectFileExtension(selectedPath);

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

                await DesktopDialogs.Messages.ShowAsync(FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveFailed_Message", typeof(FlowBloxMainUITexts)) + ex.Message,
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveFailed_Title", typeof(FlowBloxMainUITexts)),
                        MessageBoxButtons.Ok,
                        MessageBoxIcon.Error
                    );
            }

            UpdateUI();
        }

        private async void rbSaveProjectAs_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_recentProjectPath))
                return;

            try
            {
                var selectedPath = await PickSaveProjectPathAsync();
                    if (selectedPath != null)
                {
                    var newProjectPath = EnsureProjectFileExtension(selectedPath);

                    SaveActiveProjectFile(newProjectPath, generateNewProjectGuid: true);
                    _recentProjectPath = newProjectPath;
                }
            }
            catch (Exception Exception)
            {
                await DesktopDialogs.Messages.ShowAsync(FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveFailed_Message", typeof(FlowBloxMainUITexts)) + Exception.Message,
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveFailed_Title", typeof(FlowBloxMainUITexts)),
                        MessageBoxButtons.Ok,
                        MessageBoxIcon.Error
                    );
            }

            UpdateUI();
        }

        private async void itmQuitApplication_Click(object sender, EventArgs e)
        {
            await CloseApplicationControlledAsync();
        }

        private async Task CloseApplicationControlledAsync(string installerPathToLaunch = null)
        {
            _installerPathToLaunchOnClose = installerPathToLaunch;

            if (FlowBloxProjectManager.Instance.ActiveProject != null)
            {
                MessageBoxResult result = await DesktopDialogs.Messages.ShowAsync(string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveBeforeExit_Message", typeof(FlowBloxMainUITexts)), FlowBloxProjectManager.Instance.ActiveProject.ProjectName),
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveBeforeExit_Title", typeof(FlowBloxMainUITexts)),
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                if (result == MessageBoxResult.Yes)
                {
                    if (string.IsNullOrWhiteSpace(_recentProjectPath) || IsActiveProjectFromDistributedData())
                    {
                        var selectedPath = await PickSaveProjectPathAsync();
                        if (selectedPath == null)
                        {
                            _installerPathToLaunchOnClose = null;
                            return;
                        }
                        var newProjectPath = EnsureProjectFileExtension(selectedPath);
                        SaveActiveProjectFile(newProjectPath);
                        _recentProjectPath = newProjectPath;
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

        internal void SaveActiveProjectFile(string fileName, bool generateNewProjectGuid = false)
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

        private void ApplyExampleProjectMenuIcon() { }

        private void itmOptions_Click(object sender, EventArgs e)
        {
            var optionsWindow = new UICore.Views.OptionsWindow();
            WpfDialogHost.ShowDialog(optionsWindow, this);
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
            WpfDialogHost.ShowDialog(propertyViewWpf, this);
            UpdateUI();
        }

        private async void itmCloseProject_Click(object sender, EventArgs e)
        {
            if (FlowBloxProjectManager.Instance.ActiveProject == null)
                return;

            MessageBoxResult Result = await DesktopDialogs.Messages.ShowAsync(string.Format(FlowBloxResourceUtil.GetLocalizedString("AppWindow_CloseProjectConfirm_Message", typeof(FlowBloxMainUITexts)), FlowBloxProjectManager.Instance.ActiveProject.ProjectName) + "\r\n" +
                    "\r\n" +
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_SaveReminder_Message", typeof(FlowBloxMainUITexts)),
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_CloseProjectConfirm_Title", typeof(FlowBloxMainUITexts)), MessageBoxButtons.YesNo, MessageBoxIcon.Question
                );

            if (Result == MessageBoxResult.Yes)
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

                await DesktopDialogs.Messages.ShowAsync(FlowBloxResourceUtil.GetLocalizedString("AppWindow_ProjectLoadFailed_Message", typeof(FlowBloxMainUITexts)),
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_ProjectLoadFailed_Title", typeof(FlowBloxMainUITexts)),
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Warning
                );

                await DesktopDialogs.Messages.ShowAsync(FlowBloxResourceUtil.GetLocalizedString("AppWindow_ErrorReport_Message", typeof(FlowBloxMainUITexts)) + "\r\n" +
                    "\r\n" +
                    "Exception:" + "\r\n" +
                    e.ToString(),
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_ErrorReport_Title", typeof(FlowBloxMainUITexts)),
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Information
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
                await DesktopDialogs.Messages.ShowAsync(project.Notice,
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_ProjectNotice_Title", typeof(FlowBloxMainUITexts)),
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Information
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

        private void AppWindow_FormClosing(object sender, global::Avalonia.Controls.WindowClosingEventArgs e)
        {
            if (IsProjectEditingReadOnly) { e.Cancel = true; return; }
            SaveLayout();
            if (!string.IsNullOrWhiteSpace(_installerPathToLaunchOnClose)) LaunchInstaller(_installerPathToLaunchOnClose);
            _appNotificationService.NotificationsChanged -= AppNotificationService_NotificationsChanged;
        }

        private void itmToolbox_Click(object sender, EventArgs e)
        {
            var dialog = new ToolboxWindow();
            WpfDialogHost.ShowDialog(dialog, this);
        }

        private void itmEditProject_Click(object sender, EventArgs e)
        {
            if (FlowBloxProjectManager.Instance.ActiveProject == null)
                return;

            var projectView = new CreateEditProjectWindow(FlowBloxProjectManager.Instance.ActiveProject, isEditing: true);
            WpfDialogHost.ShowDialog(projectView, this);
            UpdateUI();
            UpdateUI_ProjectName();
        }

        private async void AppWindow_Load(object sender, EventArgs e)
        {
            if (!FlowBlox.AvaloniaDesktop.App.StartupOptions.EnableInteractiveStartup) return;

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
            WpfDialogHost.ShowDialog(aboutWindow, this);
            UpdateUI();
        }

        private void itmLicense_Click(object sender, EventArgs e)
        {

        }

        private async void itmSaveGridImage_Click(object sender, EventArgs e)
        {
            try
            {
                string OutputDirectory = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.PathsOutputDir].Value;
                if (!Directory.Exists(OutputDirectory))
                {
                    Directory.CreateDirectory(OutputDirectory);
                }
                var selectedPath = await DesktopDialogs.Files.SaveImageAsync(OutputDirectory);
                if (selectedPath != null)
                    _dockContentProjectPanel.SaveInnerPanelBitmap(selectedPath);
            }
            catch (Exception ex)
            {
                var logger = FlowBloxLogManager.Instance.GetLogger();
                logger.Exception(ex);

                await DesktopDialogs.Messages.ShowAsync(FlowBloxResourceUtil.GetLocalizedString("AppWindow_GridExportFailed_Message", typeof(FlowBloxMainUITexts)),
                        FlowBloxResourceUtil.GetLocalizedString("AppWindow_GridExportFailed_Title", typeof(FlowBloxMainUITexts)),
                        MessageBoxButtons.Ok,
                        MessageBoxIcon.Warning
                    );
            }

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

                var currentVersion = FlowBloxVersionHelper.ParseComparableVersion(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString());
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
            if (InvokeRequired) { BeginInvoke(new Action(RefreshNotificationBar)); return; }
            _appNotificationService.RemoveExpiredNotifications();
            var notification = _appNotificationService.GetCurrentNotification();
            NotificationBar.IsVisible = notification != null;
            NotificationMessage.Text = notification?.Message ?? string.Empty;
            NotificationAction.Content = notification?.ActionText;
            NotificationAction.IsVisible = !string.IsNullOrEmpty(notification?.ActionText);
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
                            await CloseApplicationControlledAsync(pathToInstall);
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

                DesktopDialogs.Messages.Show(string.Format(
                        FlowBloxResourceUtil.GetLocalizedString(
                            "AppWindow_OpenUrlFailed_Message",
                            typeof(FlowBloxMainUITexts)),
                        url),
                    FlowBloxResourceUtil.GetLocalizedString(
                        "AppWindow_OpenUrlFailed_Title",
                        typeof(FlowBloxMainUITexts)),
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void itmResetDockablePanels_Click(object sender, EventArgs e) { ResetLayout(); }

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
            WpfDialogHost.ShowDialog(dialog, this);
        }

        private async void itmOpenFromProjectSpace_Click(object sender, EventArgs e)
        {
            await OpenProjectWithConfirmationAsync(async () =>
            {
                var dialog = new PSProjectsWindow();
                WpfDialogHost.ShowDialog(dialog, this);

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
                WpfDialogHost.ShowDialog(dialog, this);
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
            WpfDialogHost.ShowDialog(dialog, this);
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
            WpfDialogHost.ShowDialog(dialog, this);
        }

        private void itmFlowBloxTaskManagement_Click(object sender, EventArgs e)
        {
            var dialog = new FlowBloxTaskManagementWindow();
            WpfDialogHost.ShowDialog(dialog, this);
        }

        private async void itmNotifications_Click(object sender, EventArgs e)
        {
            FlowBloxOptions.GetOptionInstance().InitDefaults(false);
            var configuration = RuntimeNotificationConfigurationStore.Load(out var loadError);
            if (!string.IsNullOrWhiteSpace(loadError))
            {
                await DesktopDialogs.Messages.ShowAsync(loadError,
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notifications_LoadError_Title", typeof(FlowBloxMainUITexts)),
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Warning);
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

            WpfDialogHost.ShowDialog(propertyWindow, this);
            if (propertyWindow.DialogResult == true &&
                !RuntimeNotificationConfigurationStore.Save(configuration, out var saveError))
            {
                await DesktopDialogs.Messages.ShowAsync(saveError,
                    FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notifications_SaveError_Title", typeof(FlowBloxMainUITexts)),
                    MessageBoxButtons.Ok,
                    MessageBoxIcon.Error);
            }
        }

        private void itmManageInputFiles_Click(object sender, EventArgs e)
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            if (project == null)
                return;

            var dialog = new ManageInputFilesWindow(project, readOnly: IsProjectEditingReadOnly);
            WpfDialogHost.ShowDialog(dialog, this);
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
                var item = new MenuEntry
                {
                    Text = BuildRecentProjectDisplayText(recentProject),
                    Tag = recentProject
                };
                item.Click += itmRecentProjectsItem_Click;
                itmRecentProjects.DropDownItems.Add(item);
            }


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
            if (sender is not MenuEntry menuItem)
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

        internal T GetAccessibleComponent<T>() where T : class { return _dockContentProjectPanel as T; }
    }
}
