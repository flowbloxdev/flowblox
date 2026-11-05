using FlowBlox.Core.Constants;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FlowBlox.UICore.ViewModels
{
    public class CreateExtensionViewModel : INotifyPropertyChanged
    {
        private string _extensionName;
        private string _extensionDescription;
        private string _userToken;
        private MetroWindow _window;

        private readonly Lazy<FlowBloxWebApiService> _flowBloxWebApiService = new Lazy<FlowBloxWebApiService>(() =>
        {
            var webApiServiceUrl = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.ApiExtensionServiceBaseUrl].Value;
            return new FlowBloxWebApiService(webApiServiceUrl);
        });

        public string ExtensionName
        {
            get => _extensionName;
            set
            {
                _extensionName = value;
                OnPropertyChanged(nameof(ExtensionName));
                OnPropertyChanged(nameof(CanCreate));
            }
        }

        public string ExtensionDescription
        {
            get => _extensionDescription;
            set
            {
                _extensionDescription = value;
                OnPropertyChanged(nameof(ExtensionDescription));
                OnPropertyChanged(nameof(CanCreate));
            }
        }

        public bool CanCreate =>
            !string.IsNullOrWhiteSpace(ExtensionName) &&
            !string.IsNullOrWhiteSpace(ExtensionDescription);

        public ICommand CreateCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ExtractMetadataCommand { get; }

        public CreateExtensionViewModel()
        {
            CreateCommand = new RelayCommand(async () => await CreateAsync());
            CancelCommand = new RelayCommand(Cancel);
            ExtractMetadataCommand = new RelayCommand(async () => await ExtractMetadataAsync());
        }

        public CreateExtensionViewModel(MetroWindow window, string userToken) : this()
        {
            _userToken = userToken;
            _window = window;
        }

        private async Task CreateAsync()
        {
            var request = new FbCreateExtensionRequest
            {
                Name = _extensionName,
                Description = _extensionDescription
            };

            var response = await _flowBloxWebApiService.Value.CreateExtensionAsync(_userToken, request);

            if (response.Success)
            {
                _window.DialogResult = true;
                _window.Close();
            }
            else
            {
                await MessageBoxHelper.ShowMessageBoxAsync(_window, MessageBoxType.Error, ApiErrorMessageHelper.BuildErrorMessage(response.ErrorMessage));
            }
        }

        private async Task ExtractMetadataAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "DLL files (*.dll)|*.dll|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            try
            {
                var metadata = LibraryMetadataExtractor.Extract(openFileDialog.FileName);

                ExtensionName = metadata.AssemblyName;
                ExtensionDescription = metadata.Description;
            }
            catch (Exception ex)
            {
                await MessageBoxHelper.ShowMessageBoxAsync(
                    _window,
                    MessageBoxType.Error,
                    ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_ExtractMetadataFailed", typeof(Resources.CreateExtensionWindow)),
                        ex.Message));
            }
        }

        private void Cancel()
        {
            _window.Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
