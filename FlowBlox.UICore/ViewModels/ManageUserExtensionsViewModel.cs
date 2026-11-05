using FlowBlox.Core.Constants;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Validation;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.Views;
using MahApps.Metro.Controls;

namespace FlowBlox.UICore.ViewModels
{
    public class ManageUserExtensionsViewModel : INotifyPropertyChanged
    {
        private Window _window;
        private object _selectedItem;
        private string _userToken;
        private string _userName;
        private bool _isExtensionDirty;
        private bool _isVersionDirty;
        private bool _isBusy;

        private Lazy<FlowBloxWebApiService> _flowBloxWebApiService = new Lazy<FlowBloxWebApiService>(() =>
        {
            var webApiServiceUrl = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.ApiExtensionServiceBaseUrl].Value;
            return new FlowBloxWebApiService(webApiServiceUrl);
        });

        public ObservableCollection<FbExtensionResult> Extensions { get; set; }

        public ICommand CreateExtensionCommand { get; }
        public ICommand DeleteExtensionCommand { get; }
        public ICommand CreateVersionCommand { get; }
        public ICommand DeleteVersionCommand { get; }
        public ICommand BrowseArchiveCommand { get; }
        public ICommand SaveChangesCommand { get; }
        public ICommand PublishVersionCommand { get; }
        public ICommand ClearVersionFileCommand { get; }
        public ICommand DownloadVersionFileCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public ManageUserExtensionsViewModel(Window window, string userToken, FbUserData userData) : this()
        {
            _userToken = userToken;
            _userName = userData.UserName;
            _window = window;

            UpdateExtensionsAndVersions(userData.UserName);
        }

        public ManageUserExtensionsViewModel()
        {
            CreateExtensionCommand = new RelayCommand(CreateExtension);
            DeleteExtensionCommand = new RelayCommand(DeleteExtension, CanDeleteExtension);
            CreateVersionCommand = new RelayCommand(CreateVersion, CanCreateVersion);
            DeleteVersionCommand = new RelayCommand(DeleteVersion, CanDeleteVersion);
            BrowseArchiveCommand = new RelayCommand(BrowseArchive);
            ClearVersionFileCommand = new RelayCommand(ClearVersionFile, CanClearVersionFile);
            DownloadVersionFileCommand = new RelayCommand(DownloadVersionFile, CanDownloadVersionFile);
            SaveChangesCommand = new RelayCommand(SaveChanges, CanSaveChanges);
            PublishVersionCommand = new RelayCommand(PublishVersion, CanPublishVersion);
            

            Extensions = new ObservableCollection<FbExtensionResult>();
        }

        public object SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSaveChanges));
                OnPropertyChanged(nameof(CanPublishVersion));
                OnPropertyChanged(nameof(SelectedExtension));
                OnPropertyChanged(nameof(SelectedVersion));

                ((RelayCommand)DeleteExtensionCommand).Invalidate();
                ((RelayCommand)CreateVersionCommand).Invalidate();
                ((RelayCommand)DeleteVersionCommand).Invalidate();
                ((RelayCommand)SaveChangesCommand).Invalidate();
                ((RelayCommand)PublishVersionCommand).Invalidate();
            }
        }

        public FbExtensionResult SelectedExtension => _selectedItem as FbExtensionResult;

        public FbVersionResult SelectedVersion => _selectedItem as FbVersionResult;

        private ExtensionContentValidator _validator = new ExtensionContentValidator();

        public bool IsExtensionSelected => SelectedItem is FbExtensionResult;

        public bool IsVersionSelected => SelectedItem is FbVersionResult;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value)
                    return;

                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSaveChanges));
                ((RelayCommand)SaveChangesCommand).Invalidate();
            }
        }

        public bool CanSaveChanges() => !IsBusy && (SelectedExtension?.IsDirty == true || SelectedVersion?.IsDirty == true);

        private readonly Dictionary<(Guid ExtensionGuid, string Version), bool> _hasVersionContentCache = new Dictionary<(Guid ExtensionGuid, string Version), bool>();
        private bool HasVersionContent(Guid extensionGuid, string version)
        {
            var key = (extensionGuid, version ?? string.Empty);

            if (_hasVersionContentCache.TryGetValue(key, out var cached))
                return cached;

            _hasVersionContentCache[key] = false;
            _ = RefreshHasVersionContentAsync(key);
            return false;
        }

        private readonly HashSet<(Guid ExtensionGuid, string Version)> _hasVersionContentRefreshing = new HashSet<(Guid ExtensionGuid, string Version)>();
        private async Task RefreshHasVersionContentAsync((Guid ExtensionGuid, string Version) key)
        {
            if (_hasVersionContentRefreshing.Contains(key))
                return;

            _hasVersionContentRefreshing.Add(key);

            try
            {
                var resp = await _flowBloxWebApiService.Value.HasVersionContentAsync(key.ExtensionGuid, key.Version);
                if (resp.Success)
                    _hasVersionContentCache[key] = resp.ResultObject;
                else
                {
                    await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, ApiErrorMessageHelper.BuildErrorMessage(resp.ErrorMessage));
                    _hasVersionContentCache[key] = false;
                }
            }
            finally
            {
                _hasVersionContentRefreshing.Remove(key);
            }
        }

        private void InvalidateHasVersionContentCache(Guid extensionGuid, string version)
        {
            var key = (extensionGuid, version);
            _hasVersionContentCache.Remove(key);
        }

        public bool CanPublishVersion()
        {
            if (SelectedVersion == null)
                return false;

            // Eine bereits veröffentlichte Version kann nicht erneut veröffentlich werden
            if (SelectedVersion.Released)
                return false;

            // Wenn Änderungen ausstehen kann die Version nicht veröffentlicht werden
            if (SelectedVersion?.IsDirty == true)
                return false;

            if (!_versionToExtension.TryGetValue(SelectedVersion, out var extension))
                return false;

            // Die Version muss aktuell einen Inhalt besitzen, damit sie veröffentlicht werden kann.
            if (!HasVersionContent(extension.Guid, SelectedVersion.Version))
                return false;

            return true;
        }

        private readonly Dictionary<FbVersionResult, FbExtensionResult> _versionToExtension = new Dictionary<FbVersionResult, FbExtensionResult>();
        private async void UpdateExtensionsAndVersions(string userName)
        {
            var resp = await _flowBloxWebApiService.Value.GetExtensionsAsync(_userToken, searchForUsername: userName);

            Extensions.Clear();
            _versionToExtension.Clear();

            if (!resp.Success)
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                    ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_LoadExtensionsFailed", typeof(Resources.ManageUserExtensionsWindow)),
                        resp.ErrorMessage));

                return;
            }


            var extensions = resp.ResultObject;
            if (extensions == null)
                return;

            foreach (var extension in extensions)
            {
                Extensions.Add(extension);
                extension.IsDirty = false;

                foreach (var version in extension.Versions)
                {
                    _versionToExtension.TryAdd(version, extension);
                    version.IsDirty = false;
                }
            }
        }


        private void CreateExtension()
        {
            var createExtensionWindow = new CreateExtensionWindow(_userToken)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _window
            };

            if (createExtensionWindow.ShowDialog() == true)
            {
                UpdateExtensionsAndVersions(_userName);
            }
        }

        private async void DeleteExtension()
        {
            if (SelectedItem is FbExtensionResult extension)
            {
                var request = new FbDeleteExtensionRequest
                {
                    ExtensionGuid = extension.Guid
                };

                var response = await _flowBloxWebApiService.Value.DeleteExtensionAsync(_userToken, request);
                if (response.Success)
                {
                    UpdateExtensionsAndVersions(_userName);
                }
                else
                {
                    await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, 
                        ApiErrorMessageHelper.BuildErrorMessage(response.ErrorMessage));
                }
            }
        }

        private bool CanDeleteExtension() => IsExtensionSelected && !SelectedExtension.Versions.Any(x => x.Released);

        private void CreateVersion()
        {
            if (SelectedItem is FbExtensionResult extension)
            {
                var createVersionWindow = new CreateExtensionVersionWindow(_userToken, extension)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = _window
                };

                if (createVersionWindow.ShowDialog() == true)
                {
                    UpdateExtensionsAndVersions(_userName);
                }
            }
        }

        private bool CanCreateVersion() => IsExtensionSelected;

        private async void DeleteVersion()
        {
            if (SelectedVersion != null)
            {
                if (!_versionToExtension.TryGetValue(SelectedVersion, out var extension))
                {
                    await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                        ApiErrorMessageHelper.BuildErrorMessage(
                            FlowBloxResourceUtil.GetLocalizedString("Message_ExtensionNotResolvable", typeof(Resources.ManageUserExtensionsWindow))));

                    return;
                }

                var request = new FbDeleteExtensionVersionRequest
                {
                    ExtensionGuid = extension.Guid,
                    VersionNumber = SelectedVersion.Version
                };

                var response = await _flowBloxWebApiService.Value.DeleteExtensionVersionAsync(_userToken, request);
                if (response.Success)
                {
                    UpdateExtensionsAndVersions(_userName);
                }
                else
                {
                    await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, 
                        ApiErrorMessageHelper.BuildErrorMessage(response.ErrorMessage));

                }
            }
        }

        private bool CanDeleteVersion() => IsVersionSelected && !SelectedVersion.Released;

        private async void BrowseArchive()
        {
            if (this.SelectedVersion != null)
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "ZIP files (*.zip)|*.zip",
                    Title = "Select an archive file"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    SelectedVersion.ArchivePath = openFileDialog.FileName;
                    await TryUpdateSelectedVersionMetadataAsync();
                }
            }
        }

        private async void SaveChanges()
        {
            try
            {
                IsBusy = true;

                if (SelectedExtension?.IsDirty == true)
                {
                    var request = new FbExtensionChangeRequest
                    {
                        ExtensionGuid = SelectedExtension.Guid.ToString(),
                        Description = SelectedExtension.Description,
                        Active = SelectedExtension.Active
                    };

                    var response = await _flowBloxWebApiService.Value.UpdateExtensionAsync(_userToken, request);

                    if (response.Success)
                    {
                        await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Notification,
                            FlowBloxResourceUtil.GetLocalizedString("Message_SaveChangesSuccessful", typeof(Resources.ManageUserExtensionsWindow)));

                        SelectedExtension.IsDirty = false;
                    }
                    else
                    {
                        await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                            ApiErrorMessageHelper.BuildErrorMessage(response.ErrorMessage));
                    }
                }

                if (SelectedVersion?.IsDirty == true)
                {
                    if (!_versionToExtension.TryGetValue(SelectedVersion, out var extension))
                    {
                        await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                            FlowBloxResourceUtil.GetLocalizedString("Message_ExtensionNotResolvable", typeof(Resources.ManageUserExtensionsWindow)));

                        return;
                    }

                    var changeRequest = new FbExtensionVersionChangeRequest
                    {
                        ExtensionGuid = extension.Guid.ToString(),
                        Version = SelectedVersion.Version,
                        Changes = SelectedVersion.Changes,
                        Content = SelectedVersion.ArchiveContent,
                        RuntimeVersion = SelectedVersion.RuntimeVersion,
                        Dependencies = SelectedVersion.Dependencies?.ToList(),
                        Active = SelectedVersion.Active,
                        BackwardsCompatible = SelectedVersion.BackwardsCompatible
                    };

                    if (SelectedVersion.ArchiveContent != null)
                    {
                        if (await IsExtensionContentValid(extension) == false)
                            return;

                        changeRequest.Content = SelectedVersion.ArchiveContent;
                    }

                    var response = await _flowBloxWebApiService.Value.UpdateExtensionVersionAsync(_userToken, changeRequest);

                    if (response.Success)
                    {
                        SelectedVersion.IsDirty = false;
                        SelectedVersion.ArchivePath = null;

                        InvalidateHasVersionContentCache(extension.Guid, SelectedVersion.Version);

                        await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Notification,
                            FlowBloxResourceUtil.GetLocalizedString("Message_SaveChangesSuccessful", typeof(Resources.ManageUserExtensionsWindow)));
                    }
                    else
                    {
                        await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                            ApiErrorMessageHelper.BuildErrorMessage(response.ErrorMessage));
                    }
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<bool> IsExtensionContentValid(FbExtensionResult extension)
        {
            if (SelectedVersion.ArchiveContent == null)
                return false;

            var result = await _validator.ValidateAsync(SelectedVersion.ArchiveContent, extension.Name, SelectedVersion.Version);
            if (result != ValidationResult.Success)
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, result.ErrorMessage);
                return false;
            }

            return true;
        }

        private async Task TryUpdateSelectedVersionMetadataAsync()
        {
            if (SelectedVersion == null || SelectedVersion.ArchiveContent == null)
                return;

            if (!_versionToExtension.TryGetValue(SelectedVersion, out var extension))
                return;

            var metadata = ExtensionContentMetadataExtractor.GetMetadataFromDepsJson(SelectedVersion.ArchiveContent, extension.Name);
            if (metadata == null)
                return;

            var versionDependencies = new List<FbVersionDependency>();
            foreach (var resolvedDependency in metadata.Dependencies)
            {
                var extResp = await _flowBloxWebApiService.Value.GetExtensionAsync(
                    new FbExtensionRequest
                    {
                        Name = resolvedDependency.Name
                    });

                if (!extResp.Success || extResp.ResultObject == null)
                    return;

                var resolvedExtension = extResp.ResultObject;
                versionDependencies.Add(new FbVersionDependency
                {
                    ExtensionName = resolvedExtension.Name,
                    ExtensionGuid = resolvedExtension.Guid,
                    Version = resolvedDependency.Version
                });
            }

            SelectedVersion.RuntimeVersion = metadata.RuntimeVersion;
            SelectedVersion.Dependencies = versionDependencies;
        }

        private bool CanClearVersionFile() => !string.IsNullOrEmpty(SelectedVersion?.ArchivePath);

        private void ClearVersionFile()
        {
            if (SelectedVersion != null)
                SelectedVersion.ArchivePath = null;
        }
        
        private bool CanDownloadVersionFile()
        {
            if (SelectedVersion == null)
                return false;

            if (!_versionToExtension.TryGetValue(SelectedVersion, out var extension))
                return false;

            return HasVersionContent(extension.Guid, SelectedVersion.Version);
        }

        private async void DownloadVersionFile()
        {
            if (SelectedVersion == null)
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, "No version selected for download.");
                return;
            }

            if (!_versionToExtension.TryGetValue(SelectedVersion, out var extension))
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                    FlowBloxResourceUtil.GetLocalizedString("Message_ExtensionNotResolvable", typeof(Resources.ManageUserExtensionsWindow)));

                return;
            }

            // Retrieving the file content (Base64-encoded) from the web API
            var contentResp = await _flowBloxWebApiService.Value.GetVersionContentAsync(extension.Guid, SelectedVersion.Version);
            if (!contentResp.Success)
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, 
                    ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_DownloadVersionContentFailed", typeof(Resources.ManageUserExtensionsWindow)), 
                        contentResp.ErrorMessage));

                return;
            }

            var base64Content = contentResp.ResultObject;
            if (string.IsNullOrEmpty(base64Content))
            {
                var msg = FlowBloxResourceUtil.GetLocalizedString(
                    "Error_DownloadVersionContentEmpty",
                    typeof(Resources.ManageUserExtensionsWindow));

                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, msg);
                return;
            }

            try
            {
                // Base64 decoding of the content
                byte[] fileContent = Convert.FromBase64String(base64Content);

                // Saving the file to the local system
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"{extension.Name}_{SelectedVersion.Version}.zip",
                    Filter = "ZIP files (*.zip)|*.zip",
                    Title = FlowBloxResourceUtil.GetLocalizedString(
                        "Title_SaveVersionFile",
                        typeof(Resources.ManageUserExtensionsWindow))
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllBytes(saveFileDialog.FileName, fileContent);

                    await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Notification,
                        FlowBloxResourceUtil.GetLocalizedString("Message_DownloadVersionFileSuccessful", typeof(Resources.ManageUserExtensionsWindow)));
                }
            }
            catch (FormatException ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);

                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, 
                    FlowBloxResourceUtil.GetLocalizedString("Error_InvalidBase64Content", typeof(Resources.ManageUserExtensionsWindow)));
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(ex);

                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, 
                    FlowBloxResourceUtil.GetLocalizedString("Error_SaveFileFailed", typeof(Resources.ManageUserExtensionsWindow)));
            }
        }

        private async void PublishVersion()
        {
            if (SelectedVersion == null)
                return;

            if (!_versionToExtension.TryGetValue(SelectedVersion, out var extension))
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, 
                    FlowBloxResourceUtil.GetLocalizedString("Message_ExtensionNotResolvable", typeof(Resources.ManageUserExtensionsWindow)));

                return;
            }

            var publishRequest = new FbExtensionVersionChangeRequest
            {
                Version = SelectedVersion.Version,
                ExtensionGuid = extension.Guid.ToString(),
                BackwardsCompatible = SelectedVersion.BackwardsCompatible,
                Active = SelectedVersion.Active,
                Released = true
            };

            var response = await _flowBloxWebApiService.Value.UpdateExtensionVersionAsync(_userToken, publishRequest);

            if (response.Success)
            {
                SelectedVersion.Released = true;
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Notification, "Version successfully published.");
            }
            else
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, ApiErrorMessageHelper.BuildErrorMessage(response.ErrorMessage));
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
