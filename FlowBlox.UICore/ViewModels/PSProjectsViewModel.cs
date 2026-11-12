using FlowBlox.Core.Constants;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.ViewModels.PSProjects;
using FlowBlox.UICore.Views;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FlowBlox.UICore.ViewModels
{
    public class PSProjectsViewModel : INotifyPropertyChanged
    {
        private Window _ownerWindow;

        private string _searchText;
        private bool _isMine;
        private bool _isDeletingProject;
        private bool _loadRemoteData = true;

        private FbProjectResult _selectedProject;
        private List<FbProjectResult> _searchResults;
        private readonly ProjectSpaceUserNameCache _creatorNameCache = new();
        private IReadOnlyDictionary<int, string> _creatorNames = new Dictionary<int, string>();

        public IReadOnlyDictionary<int, string> CreatorNames => _creatorNames;

        private FbProjectVersionResult _selectedVersion;

        public RelayCommand CloseCommand { get; }
        public RelayCommand SearchCommand { get; }
        public RelayCommand OpenProjectCommand { get; }
        public RelayCommand OpenVersionCommand { get; }
        public RelayCommand EditProjectCommand { get; }
        public RelayCommand DeleteProjectCommand { get; }

        public RelayCommand LoginCommand { get; }
        public RelayCommand RegisterCommand { get; }
        public RelayCommand LogoutCommand { get; }
        public RelayCommand EditUserPropertiesCommand { get; }

        public RelayCommand CreateVersionCommand { get; }
        public RelayCommand EditVersionCommand { get; }
        public RelayCommand DownloadVersionCommand { get; }
        public RelayCommand RefreshVersionsCommand { get; }
        public RelayCommand CopyToClipboardCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); }
        }

        public bool IsMine
        {
            get => _isMine;
            set
            {
                if (_isMine == value)
                    return;

                _isMine = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanEditProject));
                OnPropertyChanged(nameof(CanDeleteProject));
                OnPropertyChanged(nameof(CanManageVersions));
                ExecuteSearch();
            }
        }

        public List<FbProjectResult> SearchResults
        {
            get => _searchResults;
            set
            {
                _searchResults = value;
                _creatorNames = new Dictionary<int, string>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CreatorNames));
            }
        }

        public FbProjectResult SelectedProject
        {
            get => _selectedProject;
            set
            {
                if (_selectedProject == value)
                    return;

                _selectedProject = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsProjectSelected));
                OnPropertyChanged(nameof(CanEditProject));
                OnPropertyChanged(nameof(CanDeleteProject));

                // Versions are part of project DTO now.
                OnPropertyChanged(nameof(ProjectVersions));
                SelectedVersion = null;

                OnPropertyChanged(nameof(CanManageVersions));
                OnPropertyChanged(nameof(CanEditSelectedVersion));
                OnPropertyChanged(nameof(CanDownloadSelectedVersion));
                OnPropertyChanged(nameof(CanOpenVersion));
            }
        }

        public bool IsProjectSelected => SelectedProject != null;

        public List<FbProjectVersionResult> ProjectVersions
        {
            get
            {
                if (SelectedProject?.Versions == null)
                    return new List<FbProjectVersionResult>();

                return SelectedProject.Versions;
            }
        }

        public FbProjectVersionResult SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (_selectedVersion == value)
                    return;

                _selectedVersion = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVersionSelected));
                OnPropertyChanged(nameof(CanEditSelectedVersion));
                OnPropertyChanged(nameof(CanDownloadSelectedVersion));
                OnPropertyChanged(nameof(CanOpenVersion));
            }
        }

        public bool IsVersionSelected => SelectedVersion != null;

        public bool CanManageVersions => IsProjectSelected && IsMine && ActiveUser != null && !string.IsNullOrWhiteSpace(UserToken);
        public bool CanEditSelectedVersion => CanManageVersions && IsVersionSelected;
        public bool CanDownloadSelectedVersion => IsProjectSelected && IsVersionSelected;
        public bool CanOpenVersion => IsProjectSelected && IsVersionSelected;

        public FbUserData ActiveUser
        {
            get => FlowBloxAccountManager.Instance.GetActiveUser(ApiUrl);
            set
            {
                FlowBloxAccountManager.Instance.SetActiveUser(ApiUrl, value);
                OnPropertyChanged(nameof(ActiveUser));
                OnPropertyChanged(nameof(CanDeleteProject));
                OnPropertyChanged(nameof(CanLogin));
                OnPropertyChanged(nameof(CanLogout));
                OnPropertyChanged(nameof(CanToggleMyProjects));
                OnPropertyChanged(nameof(CanEditUserProperties));
                OnPropertyChanged(nameof(CanManageVersions));
            }
        }

        public string UserToken
        {
            get => FlowBloxAccountManager.Instance.GetUserToken(ApiUrl);
            set
            {
                FlowBloxAccountManager.Instance.SetUserToken(ApiUrl, value);
                OnPropertyChanged(nameof(UserToken));
                OnPropertyChanged(nameof(CanDeleteProject));
                OnPropertyChanged(nameof(CanEditUserProperties));
                OnPropertyChanged(nameof(CanManageVersions));
            }
        }

        private FbApiMetadata _apiMetadata;
        public FbApiMetadata ApiMetadata
        {
            get => _apiMetadata;
            private set
            {
                if (ReferenceEquals(_apiMetadata, value))
                    return;

                _apiMetadata = value;
                OnPropertyChanged(nameof(ApiMetadata));
                OnPropertyChanged(nameof(CanLogin));
                OnPropertyChanged(nameof(CanRegister));
                OnPropertyChanged(nameof(CanLogout));
                OnPropertyChanged(nameof(CanToggleMyProjects));
            }
        }

        public bool CanLogin => ApiMetadata?.Capabilities?.CanLogin == true && ActiveUser == null;
        public bool CanRegister => ApiMetadata?.Capabilities?.CanRegister == true;
        public bool CanLogout => ApiMetadata?.Capabilities?.CanLogin == true && ActiveUser != null;
        public bool CanToggleMyProjects => ApiMetadata?.Capabilities?.CanLogin == true && ActiveUser != null;
        public bool CanEditUserProperties => ApiMetadata?.Capabilities?.CanLogin == true && ActiveUser != null && !string.IsNullOrWhiteSpace(UserToken);

        public bool CanEditProject(object arg) => IsProjectSelected && IsMine;

        private bool IsSelectedProjectDeletable => CanManageVersions
            && SelectedProject.Versions != null
            && SelectedProject.Versions.Count == 0
            && Guid.TryParse(SelectedProject.Guid, out _);

        public bool CanDeleteProject => !_isDeletingProject && IsSelectedProjectDeletable;

        private static string ApiUrl => FlowBloxOptions.GetOptionInstance()
            .OptionCollection[OptionsConstants.ApiProjectServiceBaseUrl]
            .Value;

        private readonly Lazy<FlowBloxWebApiService> _flowBloxWebApiService = new Lazy<FlowBloxWebApiService>(() =>
        {
            return new FlowBloxWebApiService(ApiUrl);
        });

        public PSProjectsViewModel()
        {
            SearchResults = new List<FbProjectResult>();

            CloseCommand = new RelayCommand(() => _ownerWindow?.Close());
            SearchCommand = new RelayCommand(ExecuteSearch);
            OpenProjectCommand = new RelayCommand(OpenSelectedProject, _ => IsProjectSelected);
            OpenVersionCommand = new RelayCommand(_ => OpenSelectedVersion(), _ => CanOpenVersion);
            EditProjectCommand = new RelayCommand(EditSelectedProject, CanEditProject);
            DeleteProjectCommand = new RelayCommand(async () => await DeleteSelectedProjectAsync(), () => CanDeleteProject);

            LoginCommand = new RelayCommand(ExecuteLogin);
            RegisterCommand = new RelayCommand(() =>
            {
                var registrationWindow = new RegistrationWindow(ApiUrl)
                {
                    Owner = _ownerWindow
                };
                registrationWindow.ShowDialog();
            });

            LogoutCommand = new RelayCommand(() =>
            {
                ActiveUser = null;
                UserToken = null;

                if (IsMine)
                    IsMine = false;
                else
                    ExecuteSearch();
            });

            EditUserPropertiesCommand = new RelayCommand(_ => ExecuteEditUserProperties(), _ => CanEditUserProperties);

            CreateVersionCommand = new RelayCommand(_ => ExecuteCreateStableVersion(), _ => CanManageVersions);
            EditVersionCommand = new RelayCommand(_ => ExecuteEditVersionMetadata(), _ => CanEditSelectedVersion);
            DownloadVersionCommand = new RelayCommand(async _ => await ExecuteDownloadVersionAsync(), _ => CanDownloadSelectedVersion);
            RefreshVersionsCommand = new RelayCommand(async _ => await RefreshVersionsAsync(), _ => CanManageVersions);

            CopyToClipboardCommand = new RelayCommand(
                arg => CopyToClipboard(arg as string),
                arg => !string.IsNullOrWhiteSpace(arg as string));
        }

        public PSProjectsViewModel(Window ownerWindow, bool loadRemoteData = true) : this()
        {
            _ownerWindow = ownerWindow;
            _loadRemoteData = loadRemoteData;

            if (!loadRemoteData)
                return;

            ExecuteSearch();
            _ = LoadApiMetadataAsync();
        }

        private void CopyToClipboard(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            try
            {
                Clipboard.SetText(value);
            }
            catch (Exception ex)
            {
                if (_ownerWindow is MetroWindow mw)
                {
                    _ = MessageBoxHelper.ShowMessageBoxAsync(mw, MessageBoxType.Error,
                        ApiErrorMessageHelper.BuildErrorMessage(
                            FlowBloxResourceUtil.GetLocalizedString("Error_CopyToClipboardFailed", typeof(Resources.PSProjectsWindow)),
                            ex.Message));
                }
            }
        }

        private async Task LoadApiMetadataAsync()
        {
            ApiMetadata = await ApiMetadataHelper.LoadApiMetadataAsync(_flowBloxWebApiService.Value);
        }

        private void ExecuteLogin()
        {
            var loginWindow = new LoginWindow
            {
                Owner = _ownerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (loginWindow.ShowDialog() == true)
            {
                if (loginWindow.Tag != null)
                {
                    var (token, userData) = ((string, FbUserData))loginWindow.Tag;
                    ActiveUser = userData;
                    UserToken = token;
                }
                else
                {
                    ActiveUser = null;
                    UserToken = null;
                }

                ExecuteSearch();
            }
        }

        private void ExecuteEditUserProperties()
        {
            if (!CanEditUserProperties || _ownerWindow == null)
                return;

            var editUserPropertiesWindow = new EditUserPropertiesWindow(_flowBloxWebApiService.Value, UserToken, ActiveUser)
            {
                Owner = _ownerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            editUserPropertiesWindow.ShowDialog();
            OnPropertyChanged(nameof(ActiveUser));
        }

        private async void ExecuteSearch()
        {
            if (!_loadRemoteData)
                return;

            var token = IsMine ? UserToken : "";
            var endpoint = _flowBloxWebApiService.Value.BaseUrl;

            var resp = await _flowBloxWebApiService.Value.GetProjectsAsync(
                userToken: token,
                mine: IsMine,
                searchForName: SearchText ?? "");

            if (!resp.Success)
            {
                if (_ownerWindow is MetroWindow mw)
                {
                    await MessageBoxHelper.ShowMessageBoxAsync(mw, MessageBoxType.Error,
                        ApiErrorMessageHelper.BuildErrorMessage(
                            FlowBloxResourceUtil.GetLocalizedString("Error_SearchProjectsFailed", typeof(Resources.CreateOrUpdatePSProjectWindow)),
                            resp.ErrorMessage));
                }

                return;
            }

            if (resp.ResultObject != null)
            {
                SearchResults = resp.ResultObject;
                SelectedProject = null;
                _ = LoadCreatorNamesAsync(resp.ResultObject, endpoint);
            }
        }

        private async Task LoadCreatorNamesAsync(List<FbProjectResult> projects, string endpoint)
        {
            var names = new Dictionary<int, string>();
            var userIds = projects
                .Where(project => project.CreatedBy.HasValue)
                .Select(project => project.CreatedBy.Value)
                .Distinct();
            foreach (var userId in userIds)
            {
                var name = await _creatorNameCache.GetDisplayNameAsync(endpoint, userId);
                if (!ReferenceEquals(SearchResults, projects)
                    || FlowBloxApiUrlNomalizer.Normalize(endpoint) != FlowBloxApiUrlNomalizer.Normalize(ApiUrl))
                {
                    return;
                }
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names[userId] = name;
                    _creatorNames = new Dictionary<int, string>(names);
                    OnPropertyChanged(nameof(CreatorNames));
                }
            }
        }

        private void OpenSelectedProject(object obj)
        {
            OpenSelectedProject();
        }

        private void OpenSelectedProject()
        {
            if (!IsProjectSelected || _ownerWindow == null)
                return;

            _ownerWindow.Tag = new PSProjectSelection(SelectedProject);
            _ownerWindow.DialogResult = true;
            _ownerWindow.Close();
        }

        private void OpenSelectedVersion()
        {
            if (!IsProjectSelected || !IsVersionSelected || _ownerWindow == null)
                return;

            _ownerWindow.Tag = new PSProjectSelection(SelectedProject, SelectedVersion);
            _ownerWindow.DialogResult = true;
            _ownerWindow.Close();
        }

        private void EditSelectedProject(object obj)
        {
            if (!IsProjectSelected)
                return;

            if (!IsMine)
                return;

            if (_ownerWindow is not MetroWindow mw)
                return;

            var editWindow = new EditProjectMetadataWindow(_ownerWindow, _flowBloxWebApiService.Value, UserToken, SelectedProject)
            {
                Owner = _ownerWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (editWindow.ShowDialog() == true)
            {
                OnPropertyChanged(nameof(SelectedProject));
                OnPropertyChanged(nameof(SearchResults));
            }
        }

        private async Task DeleteSelectedProjectAsync()
        {
            if (!CanDeleteProject || _ownerWindow is not MetroWindow owner)
                return;

            var project = SelectedProject;
            var token = UserToken;
            _isDeletingProject = true;
            NotifyDeleteProjectAvailabilityChanged();
            try
            {
                var confirmed = await MessageBoxHelper.ShowQuestionAsync(
                    owner,
                    FlowBloxResourceUtil.GetLocalizedString("Confirm_DeleteProject_Title", typeof(Resources.PSProjectsWindow)),
                    string.Format(
                        FlowBloxResourceUtil.GetLocalizedString("Confirm_DeleteProject_Message", typeof(Resources.PSProjectsWindow)),
                        project.Name),
                    FlowBloxResourceUtil.GetLocalizedString("Button_DeleteProject", typeof(Resources.PSProjectsWindow)),
                    FlowBloxResourceUtil.GetLocalizedString("Button_CancelDeleteProject", typeof(Resources.PSProjectsWindow)));
                if (confirmed != true || !IsSelectedProjectDeletable ||
                    !ReferenceEquals(SelectedProject, project) || UserToken != token)
                    return;

                var response = await _flowBloxWebApiService.Value.DeleteProjectAsync(
                    token,
                    new FbProjectDeleteRequest { ProjectGuid = project.Guid });
                if (!response.Success)
                {
                    if (response.ErrorCode == "PROJECT_HAS_STABLE_VERSIONS")
                    {
                        await MessageBoxHelper.ShowMessageBoxAsync(owner, MessageBoxType.Warning,
                            FlowBloxResourceUtil.GetLocalizedString("Warning_ProjectHasStableVersions", typeof(Resources.PSProjectsWindow)));
                        if (ReferenceEquals(SelectedProject, project))
                            await RefreshVersionsAsync();
                    }
                    else
                    {
                        await MessageBoxHelper.ShowMessageBoxAsync(owner, MessageBoxType.Error,
                            ApiErrorMessageHelper.BuildErrorMessage(
                                FlowBloxResourceUtil.GetLocalizedString("Error_DeleteProjectFailed", typeof(Resources.PSProjectsWindow)),
                                response.ErrorMessage));
                    }
                    return;
                }

                if (ReferenceEquals(SelectedProject, project))
                    SelectedProject = null;
                SearchResults = SearchResults.Where(item => item.Guid != project.Guid).ToList();
            }
            catch (Exception exception)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to delete Project Space project.", exception);
                await MessageBoxHelper.ShowMessageBoxAsync(owner, MessageBoxType.Error,
                    ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_DeleteProjectFailed", typeof(Resources.PSProjectsWindow)),
                        exception.Message));
            }
            finally
            {
                _isDeletingProject = false;
                NotifyDeleteProjectAvailabilityChanged();
            }
        }

        private void NotifyDeleteProjectAvailabilityChanged()
        {
            OnPropertyChanged(nameof(CanDeleteProject));
            DeleteProjectCommand.Invalidate();
        }

        private async Task RefreshVersionsAsync()
        {
            if (!CanManageVersions || SelectedProject == null)
                return;

            var project = SelectedProject;
            var token = UserToken;
            if (!Guid.TryParse(project.Guid, out var projectGuid))
                return;

            var resp = await _flowBloxWebApiService.Value.GetProjectVersionsAsync(token, projectGuid);
            if (!ReferenceEquals(SelectedProject, project) || UserToken != token)
                return;

            if (!resp.Success)
            {
                if (_ownerWindow is MetroWindow mw)
                {
                    await MessageBoxHelper.ShowMessageBoxAsync(mw, MessageBoxType.Error,
                        ApiErrorMessageHelper.BuildErrorMessage(
                            FlowBloxResourceUtil.GetLocalizedString("Error_LoadProjectVersionsFailed", typeof(Resources.PSProjectsWindow)),
                            resp.ErrorMessage));
                }

                return;
            }

            project.Versions = resp.ResultObject;
            OnPropertyChanged(nameof(ProjectVersions));
            NotifyDeleteProjectAvailabilityChanged();
            SelectedVersion = null;
        }

        private async void ExecuteCreateStableVersion()
        {
            if (!CanManageVersions || SelectedProject == null)
                return;

            if (_ownerWindow is not MetroWindow owner)
                return;

            var win = new CreateProjectVersionWindow(
                owner,
                _flowBloxWebApiService.Value,
                UserToken,
                SelectedProject);

            win.Owner = owner;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (win.ShowDialog() == true)
            {
                await RefreshVersionsAsync();
            }
        }

        private void ExecuteEditVersionMetadata()
        {
            if (!CanEditSelectedVersion || SelectedProject == null || SelectedVersion == null)
                return;

            if (_ownerWindow is not MetroWindow owner)
                return;

            var win = new EditProjectVersionMetadataWindow(
                owner,
                _flowBloxWebApiService.Value,
                UserToken,
                SelectedProject,
                SelectedVersion);

            win.Owner = owner;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (win.ShowDialog() == true)
            {
                OnPropertyChanged(nameof(ProjectVersions));
                OnPropertyChanged(nameof(SelectedVersion));
            }
        }

        private async Task ExecuteDownloadVersionAsync()
        {
            if (!IsProjectSelected || !IsVersionSelected || SelectedProject == null || SelectedVersion == null)
                return;

            if (_ownerWindow is not MetroWindow mw)
                return;

            if (!Guid.TryParse(SelectedProject.Guid, out var projectGuid))
                return;

            var sfd = new SaveFileDialog
            {
                Filter = "FlowBlox Project Package (*.zip)|*.zip|All files (*.*)|*.*",
                FileName = $"{SelectedProject.Name}_v{SelectedVersion.VersionNumber}.zip"
            };

            if (sfd.ShowDialog() != true)
                return;

            var token = !string.IsNullOrWhiteSpace(UserToken) ? UserToken : "";

            var resp = await _flowBloxWebApiService.Value.GetProjectVersionContentAsync(token, projectGuid, SelectedVersion.VersionNumber);
            if (!resp.Success || string.IsNullOrWhiteSpace(resp.ResultObject))
            {
                await MessageBoxHelper.ShowMessageBoxAsync(mw, MessageBoxType.Error,
                    ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_DownloadProjectVersionFailed", typeof(Resources.PSProjectsWindow)),
                        resp.ErrorMessage));

                return;
            }

            try
            {
                var bytes = Convert.FromBase64String(resp.ResultObject);
                File.WriteAllBytes(sfd.FileName, bytes);

                await MessageBoxHelper.ShowMessageBoxAsync(mw, MessageBoxType.Notification,
                    FlowBloxResourceUtil.GetLocalizedString("Message_ProjectVersionDownloaded", typeof(Resources.PSProjectsWindow)));
            }
            catch (Exception ex)
            {
                await MessageBoxHelper.ShowMessageBoxAsync(mw, MessageBoxType.Error,
                    ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_DownloadProjectVersionFailed", typeof(Resources.PSProjectsWindow)),
                        ex.Message));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
