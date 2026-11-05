using FlowBlox.Core.Constants;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FlowBlox.UICore.ViewModels
{
    public class CreateOrUpdatePSProjectViewModel : INotifyPropertyChanged
    {
        private readonly Window _ownerWindow;
        private readonly FlowBloxProject _project;
        private readonly Action<FlowBloxProject> _prepareProjectForSave;
        private readonly Action<FlowBloxProject> _restoreProjectAfterSaveAttempt;

        private readonly string _initialProjectGuid;
        private readonly Guid _initialInternalProjectGuid;
        private readonly bool _hasPersistedProjectOrigin;
        private string _backupProjectGuid;
        private bool _newProjectIdentityCreated;

        private bool _suppressTabEffects;
        private bool _isBusy;

        public RelayCommand CloseCommand { get; }
        public RelayCommand SaveCommand { get; }

        private string _errorText;
        public string ErrorText
        {
            get => _errorText;
            set
            {
                if (_errorText == value)
                    return;

                _errorText = value;
                OnPropertyChanged();
            }
        }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex == value)
                    return;

                _selectedTabIndex = value;
                OnPropertyChanged();

                if (_suppressTabEffects)
                    return;

                if (_selectedTabIndex == 0)
                    ApplyCreateNewMode();
                else
                    ApplyUpdateExistingMode();
            }
        }

        public bool HasInitialProjectGuid => !string.IsNullOrWhiteSpace(_initialProjectGuid);

        /// <summary>
        /// If a project already has a Project Space GUID, we show the Update tab.
        /// If not, we are in Create-only mode and hide the Update tab completely.
        /// </summary>
        public bool ShowUpdateTab => HasInitialProjectGuid;

        public bool HasProjectGuid => !string.IsNullOrWhiteSpace(ProjectGuid);

        public bool CanSelectUpdateTab => HasProjectGuid || HasInitialProjectGuid;

        private string _projectGuid;
        public string ProjectGuid
        {
            get => _projectGuid;
            set
            {
                if (_projectGuid == value)
                    return;

                _projectGuid = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasProjectGuid));
                OnPropertyChanged(nameof(CanSelectUpdateTab));
                OnPropertyChanged(nameof(ShowUpdateTab));
            }
        }

        private string _projectName;
        public string ProjectName
        {
            get => _projectName;
            set
            {
                if (_projectName == value)
                    return;

                _projectName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
            }
        }

        private string _projectDescription;
        public string ProjectDescription
        {
            get => _projectDescription;
            set
            {
                if (_projectDescription == value)
                    return;

                _projectDescription = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
            }
        }

        private FbProjectVisibility _visibility = FbProjectVisibility.Private;
        public FbProjectVisibility Visibility
        {
            get => _visibility;
            set
            {
                if (_visibility == value)
                    return;

                _visibility = value;
                OnPropertyChanged();
            }
        }

        private string _createdAtText;
        public string CreatedAtText
        {
            get => _createdAtText;
            set
            {
                if (_createdAtText == value)
                    return;

                _createdAtText = value;
                OnPropertyChanged();
            }
        }

        private string _updatedAtText;
        public string UpdatedAtText
        {
            get => _updatedAtText;
            set
            {
                if (_updatedAtText == value)
                    return;

                _updatedAtText = value;
                OnPropertyChanged();
            }
        }

        public bool CanSave => ActiveUser != null
                               && !string.IsNullOrWhiteSpace(UserToken)
                               && !string.IsNullOrWhiteSpace(ProjectName)
                               && !string.IsNullOrWhiteSpace(ProjectDescription)
                               && !IsBusy;

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value)
                    return;

                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSave));
                SaveCommand.Invalidate();
            }
        }

        public FbUserData ActiveUser
        {
            get => FlowBloxAccountManager.Instance.GetActiveUser(ApiUrl);
            set
            {
                FlowBloxAccountManager.Instance.SetActiveUser(ApiUrl, value);
                OnPropertyChanged(nameof(ActiveUser));
                OnPropertyChanged(nameof(CanSave));
            }
        }

        private bool IsLoggedIn => ActiveUser != null && !string.IsNullOrWhiteSpace(UserToken);

        public string UserToken
        {
            get => FlowBloxAccountManager.Instance.GetUserToken(ApiUrl);
            set
            {
                FlowBloxAccountManager.Instance.SetUserToken(ApiUrl, value);
                OnPropertyChanged(nameof(UserToken));
                OnPropertyChanged(nameof(CanSave));
            }
        }

        private static string ApiUrl => FlowBloxOptions.GetOptionInstance()
            .OptionCollection[OptionsConstants.ApiProjectServiceBaseUrl]
            .Value;

        private readonly Lazy<FlowBloxWebApiService> _flowBloxWebApiService = new Lazy<FlowBloxWebApiService>(() =>
        {
            return new FlowBloxWebApiService(ApiUrl);
        });

        public CreateOrUpdatePSProjectViewModel()
        {
            CloseCommand = new RelayCommand(() => _ownerWindow?.Close());
            SaveCommand = new RelayCommand(async () => await ExecuteSaveAsync());
        }

        public CreateOrUpdatePSProjectViewModel(
            Window ownerWindow,
            FlowBloxProject project,
            Action<FlowBloxProject> prepareProjectForSave = null,
            Action<FlowBloxProject> restoreProjectAfterSaveAttempt = null) : this()
        {
            _ownerWindow = ownerWindow;
            _project = project;
            _prepareProjectForSave = prepareProjectForSave;
            _restoreProjectAfterSaveAttempt = restoreProjectAfterSaveAttempt;
            _initialInternalProjectGuid = project.ProjectGuid;
            _hasPersistedProjectOrigin =
                !string.IsNullOrWhiteSpace(project.ProjectFilePath) ||
                !string.IsNullOrWhiteSpace(project.ProjectSpaceGuid);

            // Only if the stored endpoint matches the current API environment, the existing ProjectSpaceGuid is considered valid for update.
            if (string.Equals(_project.ProjectSpaceEndpointUri, ApiUrl, StringComparison.OrdinalIgnoreCase))
            {
                _initialProjectGuid = project.ProjectSpaceGuid;
                ProjectGuid = _initialProjectGuid;
            }

            // Auto-select tab: Update when GUID exists, otherwise Create.
            _suppressTabEffects = true;
            SelectedTabIndex = HasInitialProjectGuid ? 1 : 0;
            _suppressTabEffects = false;

            OnPropertyChanged(nameof(ShowUpdateTab));

            InitializeFromLocalOrRemoteAsync();
            _ = EvaluateLoggedInAsync();
        }

        private async Task EvaluateLoggedInAsync()
        {
            if (IsLoggedIn)
                return;

            await ShowNotificationAsync(
                FlowBloxResourceUtil.GetLocalizedString(
                    "CreateOrUpdatePSProjectWindow_Message_LoginRequired",
                    typeof(Resources.CreateOrUpdatePSProjectWindow)));

            OnPropertyChanged(nameof(CanSave));
        }

        private async Task ShowErrorAsync(string message)
        {
            ErrorText = message;
            await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_ownerWindow, MessageBoxType.Error, message);
        }

        private async Task ShowWarningAsync(string message)
        {
            await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_ownerWindow, MessageBoxType.Question, message);
        }

        private async Task ShowNotificationAsync(string message)
        {
            ErrorText = string.Empty;
            await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_ownerWindow, MessageBoxType.Notification, message);
        }

        private void ApplyCreateNewMode()
        {
            ErrorText = string.Empty;

            if (!string.IsNullOrWhiteSpace(ProjectGuid))
                _backupProjectGuid = ProjectGuid;

            ProjectGuid = null;
            CreatedAtText = string.Empty;
            UpdatedAtText = string.Empty;
            Visibility = FbProjectVisibility.Private;

            InitializeFromLocalOrRemoteAsync();
        }

        private void ApplyUpdateExistingMode()
        {
            ErrorText = string.Empty;

            if (_newProjectIdentityCreated)
            {
                _project.ProjectGuid = _initialInternalProjectGuid;
                _newProjectIdentityCreated = false;
            }

            var restoreGuid = !string.IsNullOrWhiteSpace(_initialProjectGuid)
                ? _initialProjectGuid
                : _backupProjectGuid;

            if (string.IsNullOrWhiteSpace(restoreGuid))
            {
                _suppressTabEffects = true;
                SelectedTabIndex = 0;
                _suppressTabEffects = false;

                ApplyCreateNewMode();
                return;
            }

            ProjectGuid = restoreGuid;
            _project.ProjectSpaceGuid = restoreGuid;

            InitializeFromLocalOrRemoteAsync();
        }

        private async void InitializeFromLocalOrRemoteAsync()
        {
            ErrorText = string.Empty;

            if (!string.IsNullOrWhiteSpace(ProjectGuid) && !string.IsNullOrWhiteSpace(UserToken))
            {
                var remoteResp = await _flowBloxWebApiService.Value.GetProjectAsync(
                    new FbProjectRequest { Guid = ProjectGuid },
                    UserToken);

                if (remoteResp.Success && remoteResp.ResultObject != null)
                {
                    var remote = remoteResp.ResultObject;

                    ProjectName = remote.Name;
                    ProjectDescription = remote.Description;
                    Visibility = remote.Visibility;
                    CreatedAtText = remote.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                    UpdatedAtText = remote.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                    return;
                }
                else if (!remoteResp.Success)
                {
                    await ShowErrorAsync(ApiErrorMessageHelper.BuildErrorMessage(remoteResp.ErrorMessage));
                }
            }

            // Fallback: local project defaults
            ProjectName = _project.ProjectName;
            ProjectDescription = _project.ProjectDescription;
            CreatedAtText = string.Empty;
            UpdatedAtText = string.Empty;
        }

        private async Task ExecuteSaveAsync()
        {
            ErrorText = string.Empty;

            if (ActiveUser == null || string.IsNullOrWhiteSpace(UserToken))
            {
                await ShowErrorAsync(
                    FlowBloxResourceUtil.GetLocalizedString(
                        "Error_NotLoggedIn",
                        typeof(Resources.CreateOrUpdatePSProjectWindow)));

                return;
            }

            try
            {
                IsBusy = true;
                await WarnIfExternalProjectReferencesExistAsync();

                if (string.IsNullOrWhiteSpace(ProjectGuid))
                {
                    // Create project metadata
                    var create = await _flowBloxWebApiService.Value.CreateProjectAsync(UserToken, new FbProjectCreateRequest
                    {
                        Name = ProjectName,
                        Description = ProjectDescription,
                        Visibility = Visibility
                    });

                    if (create == null || !create.Success || string.IsNullOrWhiteSpace(create.ProjectGuid))
                    {
                        await ShowErrorAsync(
                            ApiErrorMessageHelper.BuildErrorMessage(
                                FlowBloxResourceUtil.GetLocalizedString("Error_CreateFailed", typeof(Resources.CreateOrUpdatePSProjectWindow)),
                                create?.ErrorMessage));

                        return;
                    }

                    ProjectGuid = create.ProjectGuid;
                    _project.ProjectSpaceGuid = ProjectGuid;
                }
                else
                {
                    // Update metadata
                    var updateMeta = await _flowBloxWebApiService.Value.UpdateProjectAsync(UserToken, new FbProjectChangeRequest
                    {
                        ProjectGuid = ProjectGuid,
                        Name = ProjectName,
                        Description = ProjectDescription,
                        Visibility = Visibility
                    });

                    if (updateMeta == null || !updateMeta.Success)
                    {
                        await ShowErrorAsync(
                            ApiErrorMessageHelper.BuildErrorMessage(
                                FlowBloxResourceUtil.GetLocalizedString("Error_UpdateFailed", typeof(Resources.CreateOrUpdatePSProjectWindow)),
                                updateMeta?.ErrorMessage));

                        return;
                    }
                }

                // Upload content ZIP
                _prepareProjectForSave?.Invoke(_project);
                FlowBloxWebApiService.ApiResponse saveContent;
                var generateNewProjectGuid = SelectedTabIndex == 0
                    && _hasPersistedProjectOrigin
                    && !_newProjectIdentityCreated;
                try
                {
                    saveContent = await _project.SaveToProjectSpaceAsync(
                        ProjectGuid,
                        UserToken,
                        _flowBloxWebApiService.Value,
                        generateNewProjectGuid);
                }
                finally
                {
                    _restoreProjectAfterSaveAttempt?.Invoke(_project);
                }

                if (saveContent == null || !saveContent.Success)
                {
                    await ShowErrorAsync(ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_UploadFailed", typeof(Resources.CreateOrUpdatePSProjectWindow)),
                        saveContent?.ErrorMessage));

                    return;
                }

                if (generateNewProjectGuid)
                    _newProjectIdentityCreated = true;

                // Refresh timestamps
                var remoteResp = await _flowBloxWebApiService.Value.GetProjectAsync(new FbProjectRequest { Guid = ProjectGuid }, UserToken);
                if (remoteResp.Success && remoteResp.ResultObject != null)
                {
                    var remote = remoteResp.ResultObject;

                    CreatedAtText = remote.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                    UpdatedAtText = remote.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
                }
                else if (!remoteResp.Success)
                {
                    await ShowErrorAsync(ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_RefreshMetadataFailed", typeof(Resources.CreateOrUpdatePSProjectWindow)),
                        remoteResp.ErrorMessage));

                    return;
                }

                await ShowNotificationAsync(
                    FlowBloxResourceUtil.GetLocalizedString("Message_SaveSuccessful", typeof(Resources.CreateOrUpdatePSProjectWindow)));

                _ownerWindow.DialogResult = true;
                _ownerWindow.Close();
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                await ShowErrorAsync(ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task WarnIfExternalProjectReferencesExistAsync()
        {
            // We must warn before saving if ExecuteProjectFlowBlocks reference external project files.
            // Reason: The upload goes into Project Space; file path references may break and must be migrated to Project Space GUID references.

            var blocks = _project?.FlowBlocks?
                .OfType<ExecuteProjectFlowBlock>()
                .Where(x => !string.IsNullOrWhiteSpace(x.ProjectFile))
                .ToList();

            if (blocks == null || blocks.Count == 0)
                return;

            var maxExamples = 5;
            var examples = blocks
                .Take(maxExamples)
                .Select(x => x.ProjectFile?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            var exampleList = examples.Count > 0
                ? string.Join(Environment.NewLine, examples.Select(e => $"- {e}"))
                : "- (no example available)";

            var message = FlowBloxResourceUtil.GetLocalizedString(
                "CreateOrUpdatePSProjectWindow_Warning_ExternalProjectReferences",
                typeof(Resources.CreateOrUpdatePSProjectWindow));

            message = string.Format(
                message,
                blocks.Count,
                maxExamples,
                exampleList);

            await ShowWarningAsync(message);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
