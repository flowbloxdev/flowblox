using FlowBlox.Core.Constants;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.ViewModels.Login;
using FlowBlox.UICore.Views;
using MahApps.Metro.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace FlowBlox.UICore.ViewModels
{
    public class LoginWindowViewModel : INotifyPropertyChanged
    {
        private readonly Window _window;

        public event PropertyChangedEventHandler PropertyChanged;

        private string _email;
        private string _password;
        private bool _saveUserCredentials;
        private string _selectedApiUrl;

        public ObservableCollection<string> ApiUrls { get; } = new ObservableCollection<string>();

        public bool IsApiUrlSelectionEnabled => ApiUrls.Count > 1;

        public string SelectedApiUrl
        {
            get => _selectedApiUrl;
            set
            {
                if (_selectedApiUrl != value)
                {
                    _selectedApiUrl = value;
                    OnPropertyChanged(nameof(SelectedApiUrl));
                    _ = LoadApiMetadataAsync();
                }
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged();
            }
        }

        public bool SaveUserCredentials
        {
            get => _saveUserCredentials;
            set
            {
                _saveUserCredentials = value;
                OnPropertyChanged();
            }
        }

        private FbApiMetadata _apiMetadata;
        public FbApiMetadata ApiMetadata
        {
            get => _apiMetadata;
            private set
            {
                if (!ReferenceEquals(_apiMetadata, value))
                {
                    _apiMetadata = value;
                    OnPropertyChanged(nameof(ApiMetadata));
                    OnPropertyChanged(nameof(CanResetPassword));
                }
            }
        }

        public bool CanResetPassword => ApiMetadata?.Capabilities?.CanRequestPasswordReset == true;

        public ICommand ForgotPasswordCommand { get; }
        public ICommand LoginCommand { get; }
        public ICommand CancelCommand { get; }

        public LoginWindowViewModel()
        {
            LoginCommand = new RelayCommand(async () => await LoginAsync());
            CancelCommand = new RelayCommand(() => _window?.Close());
            ForgotPasswordCommand = new RelayCommand(OpenPasswordResetWindow);
        }

        public LoginWindowViewModel(Window window, string preselectedApiUrl = null) : this()
        {
            _window = window;
            LoadApiUrls(preselectedApiUrl);
            LoadStoredLoginDataForSelectedApi();
            _ = LoadApiMetadataAsync();
        }

        private async Task LoadApiMetadataAsync()
        {
            var webApiService = new FlowBloxWebApiService(SelectedApiUrl);
            ApiMetadata = await ApiMetadataHelper.LoadApiMetadataAsync(webApiService);
            if (ApiMetadata == null)
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                    FlowBloxResourceUtil.GetLocalizedString("Error_LoadApiMetadataFailed", typeof(Resources.ExtensionsWindow)));
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OpenPasswordResetWindow()
        {
            var passwordResetWindow = new PasswordResetWindow(SelectedApiUrl)
            {
                Owner = _window
            };
            passwordResetWindow.ShowDialog();
        }

        private void LoadApiUrls(string preselectedApiUrl)
        {
            var options = FlowBloxOptions.GetOptionInstance();

            var projectUrl = options.GetOption(OptionsConstants.ApiProjectServiceBaseUrl)?.Value;
            var extensionUrl = options.GetOption(OptionsConstants.ApiExtensionServiceBaseUrl)?.Value;

            var urls = new[] { projectUrl, extensionUrl }
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => NormalizeApiUrl(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            ApiUrls.Clear();
            foreach (var u in urls)
                ApiUrls.Add(u);

            OnPropertyChanged(nameof(IsApiUrlSelectionEnabled));

            var normalizedPreselect = NormalizeApiUrl(preselectedApiUrl);

            if (!string.IsNullOrWhiteSpace(normalizedPreselect) &&
                ApiUrls.Any(u => string.Equals(u, normalizedPreselect, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedApiUrl = ApiUrls.First(u => string.Equals(u, normalizedPreselect, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                SelectedApiUrl = ApiUrls.FirstOrDefault();
            }
        }

        private void LoadStoredLoginDataForSelectedApi()
        {
            try
            {
                var storedList = FlowBloxLoginDataStorage.LoadLoginDataList(_window);
                if (storedList == null ||
                    storedList.Count == 0 ||
                    string.IsNullOrWhiteSpace(SelectedApiUrl))
                    return;

                var match = storedList.FirstOrDefault(x =>
                    string.Equals(NormalizeApiUrl(x.ApiUrl), NormalizeApiUrl(SelectedApiUrl), StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    Email = match.Username;
                    Password = match.Password;
                    SaveUserCredentials = true;
                }
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error($"Failed to load stored login data: {ex.Message}", ex);
            }
        }

        private async Task LoginAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SelectedApiUrl))
                {
                    var msg = FlowBloxResourceUtil.GetLocalizedString("Message_Login_NoApiUrlSelected", typeof(Resources.LoginWindow));
                    await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, msg);
                    return;
                }

                var service = new FlowBloxWebApiService(SelectedApiUrl);

                var tokenResp = await service.LoginAsync(new FbLoginData
                {
                    ApiUrl = SelectedApiUrl,
                    Username = Email,
                    Password = Password
                });

                if (!tokenResp.Success || string.IsNullOrWhiteSpace(tokenResp.ResultObject))
                {
                    await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                        ApiErrorMessageHelper.BuildErrorMessage(
                            FlowBloxResourceUtil.GetLocalizedString("Message_Login_InvalidCredentials", typeof(Resources.LoginWindow)),
                            apiErrorMessage: null));

                    return;
                }

                var token = tokenResp.ResultObject;

                var userDataResp = await service.GetUserInfoAsync(token);
                if (!userDataResp.Success || userDataResp.ResultObject == null)
                {
                    await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                        ApiErrorMessageHelper.BuildErrorMessage(
                            FlowBloxResourceUtil.GetLocalizedString("Message_Login_GetUserDataFailed", typeof(Resources.LoginWindow)),
                            userDataResp.ErrorMessage));

                    return;
                }

                var userData = userDataResp.ResultObject;

                FlowBloxAccountManager.Instance.SetSession(SelectedApiUrl, userData, token);

                if (SaveUserCredentials)
                {
                    FlowBloxLoginDataStorage.UpsertLoginData(new FbLoginData
                    {
                        ApiUrl = SelectedApiUrl,
                        Username = Email,
                        Password = Password
                    });
                }

                _window.DialogResult = true;
                _window.Tag = (token, userData);
                _window.Close();
            }
            catch (Exception ex)
            {
                var msg = FlowBloxResourceUtil.GetLocalizedString("Message_Login_Failed", typeof(Resources.LoginWindow));
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error, msg);
                FlowBloxLogManager.Instance.GetLogger().Error(msg, ex);
            }
        }

        private static string NormalizeApiUrl(string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
                return apiUrl;

            var normalized = apiUrl.Trim();
            return normalized.EndsWith("/") ?
                normalized.TrimEnd('/') :
                normalized;
        }
    }
}