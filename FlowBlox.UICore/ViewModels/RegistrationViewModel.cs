using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Models;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.Controls;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FlowBlox.UICore.ViewModels
{
    public class RegistrationViewModel : INotifyPropertyChanged
    {
        private string _firstName;
        private string _lastName;
        private string _userName;
        private string _email;
        private string _emailRepeat;
        private string _password;
        private string _passwordRepeat;
        private string _captchaCode;
        private ConvertedCaptchaResponse _captchaResponse;
        private string _errorText;
        private bool _canRegister;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FirstName
        {
            get => _firstName;
            set { _firstName = value; OnPropertyChanged(nameof(FirstName)); ValidateInput(); }
        }

        public string LastName
        {
            get => _lastName;
            set { _lastName = value; OnPropertyChanged(nameof(LastName)); ValidateInput(); }
        }

        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(nameof(UserName)); ValidateInput(); }
        }

        public string Email
        {
            get => _email;
            set { _email = value; OnPropertyChanged(nameof(Email)); ValidateInput(); }
        }

        public string EmailRepeat
        {
            get => _emailRepeat;
            set { _emailRepeat = value; OnPropertyChanged(nameof(EmailRepeat)); ValidateInput(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(nameof(Password)); ValidateInput(); }
        }

        public string PasswordRepeat
        {
            get => _passwordRepeat;
            set { _passwordRepeat = value; OnPropertyChanged(nameof(PasswordRepeat)); ValidateInput(); }
        }

        public string CaptchaCode
        {
            get => _captchaCode;
            set { _captchaCode = value; OnPropertyChanged(nameof(CaptchaCode)); ValidateInput(); }
        }

        public ConvertedCaptchaResponse CaptchaResponse
        {
            get => _captchaResponse;
            set { _captchaResponse = value; OnPropertyChanged(nameof(CaptchaResponse)); }
        }

        public string ErrorText
        {
            get => _errorText;
            set { _errorText = value; OnPropertyChanged(nameof(ErrorText)); }
        }

        public bool CanRegister
        {
            get => _canRegister;
            set { _canRegister = value; OnPropertyChanged(nameof(CanRegister)); }
        }

        public ICommand RegisterCommand { get; }

        public ICommand CloseCommand { get; }

        public RegistrationViewModel()
        {
            RegisterCommand = new RelayCommand(Register, () => CanRegister);
            CloseCommand = new RelayCommand(Close, () => true);
        }

        public RegistrationViewModel(Window window, string apiUrl) : this()
        {
            this._window = window;
            this._apiUrl = apiUrl;
            this._flowBloxWebApiService = new Lazy<FlowBloxWebApiService>(() =>
            {
                return new FlowBloxWebApiService(_apiUrl);
            });
            LoadCaptcha();
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void ValidateInput()
        {
            ErrorText = string.Empty;
            CanRegister = !string.IsNullOrEmpty(Email) &&
                          !string.IsNullOrEmpty(EmailRepeat) &&
                          !string.IsNullOrEmpty(Password) &&
                          !string.IsNullOrEmpty(PasswordRepeat) &&
                          !string.IsNullOrEmpty(CaptchaCode);

            if (Email != EmailRepeat)
            {
                ErrorText = FlowBloxResourceUtil.GetLocalizedString("Message_EmailMismatch", typeof(Resources.RegistrationWindow));
                CanRegister = false;
            }
            else if (Password != PasswordRepeat)
            {
                ErrorText = FlowBloxResourceUtil.GetLocalizedString("Message_PasswordMismatch", typeof(Resources.RegistrationWindow));
                CanRegister = false;
            }

            // Aktualisieren des RegisterCommand-CanExecute-Status
            ((RelayCommand)RegisterCommand).Invalidate();
        }

        public static BitmapImage ConvertBase64ToBitmapImage(string base64Data)
        {
            byte[] imageBytes = Convert.FromBase64String(base64Data);
            MemoryStream ms = new MemoryStream(imageBytes);
            BitmapImage bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = ms;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }

        private async void LoadCaptcha()
        {
            var resp = await _flowBloxWebApiService.Value.GetCaptchaAsync();

            if (!resp.Success || resp.ResultObject == null)
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Error,
                    ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_LoadCaptchaFailed", typeof(Resources.RegistrationWindow)),
                        resp.ErrorMessage));

                return;
            }

            var result = resp.ResultObject;

            this.CaptchaResponse = new ConvertedCaptchaResponse
            {
                CaptchaId = result.CaptchaId,
                CaptchaImage = ConvertBase64ToBitmapImage(result.CaptchaImageBase64)
            };
        }


        private Lazy<FlowBloxWebApiService> _flowBloxWebApiService;
        private Window _window;
        private string _apiUrl;

        private void Close()
        {
            _window.DialogResult = false;
            _window.Close();
        }

        private async void Register()
        {
            FbRegistrationData registrationData = new FbRegistrationData
            {
                FirstName = FirstName,
                LastName = LastName,
                UserName = UserName,
                EMail = Email,
                Password = Password,
                CaptchaId = this.CaptchaResponse.CaptchaId,
                CaptchaCode = this.CaptchaCode
            };

            var result = await _flowBloxWebApiService.Value.Register(registrationData);
            if (result.Success)
            {
                await MessageBoxHelper.ShowMessageBoxAsync((MetroWindow)_window, MessageBoxType.Notification,
                    FlowBloxResourceUtil.GetLocalizedString("Message_Success", typeof(Resources.RegistrationWindow)));
                _window.DialogResult = true;
            }
            else
            {
                switch (result.ErrorCode)
                {
                    case "UsernameAlreadyExists":
                        {
                            this.ErrorText = string.Format(
                                FlowBloxResourceUtil.GetLocalizedString("Message_UsernameExists", typeof(Resources.RegistrationWindow)),
                                this.UserName);
                            LoadCaptcha();
                            break;
                        }
                    case "EMailAlreadyExists":
                        {
                            this.ErrorText = string.Format(
                                FlowBloxResourceUtil.GetLocalizedString("Message_EmailExists", typeof(Resources.RegistrationWindow)),
                                this.Email);
                            LoadCaptcha();
                            break;
                        }
                    case "InvalidCaptcha":
                        {
                            this.ErrorText = FlowBloxResourceUtil.GetLocalizedString("Message_InvalidCaptcha", typeof(Resources.RegistrationWindow));
                            LoadCaptcha();
                            break;
                        }
                    default:
                        {
                            this.ErrorText = FlowBloxResourceUtil.GetLocalizedString("Message_ServiceError", typeof(Resources.RegistrationWindow)) + result.ErrorMessage;
                            break;
                        }
                }
            }
        }
    }
}
