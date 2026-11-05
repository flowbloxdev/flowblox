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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FlowBlox.UICore.ViewModels
{
    public class CreateExtensionVersionViewModel : INotifyPropertyChanged
    {
        private string _version;
        private string _detectedAssemblyName;
        private string _detectedDescription;
        private string _userToken;
        private FbExtensionResult _extension;
        private MetroWindow _window;

        private readonly Lazy<FlowBloxWebApiService> _flowBloxWebApiService = new Lazy<FlowBloxWebApiService>(() =>
        {
            var webApiServiceUrl = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.ApiExtensionServiceBaseUrl].Value;
            return new FlowBloxWebApiService(webApiServiceUrl);
        });

        public string Version
        {
            get => _version;
            set
            {
                _version = value;
                OnPropertyChanged(nameof(Version));
                OnPropertyChanged(nameof(CanCreate));
            }
        }

        public string DetectedAssemblyName
        {
            get => _detectedAssemblyName;
            set
            {
                _detectedAssemblyName = value;
                OnPropertyChanged(nameof(DetectedAssemblyName));
            }
        }

        public string DetectedDescription
        {
            get => _detectedDescription;
            set
            {
                _detectedDescription = value;
                OnPropertyChanged(nameof(DetectedDescription));
            }
        }

        public bool CanCreate => !string.IsNullOrWhiteSpace(Version) && Regex.IsMatch(Version, @"^\d+\.\d+\.\d+$");

        public ICommand CreateCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ExtractMetadataCommand { get; }

        public CreateExtensionVersionViewModel()
        {
            CreateCommand = new RelayCommand(async () => await CreateAsync());
            CancelCommand = new RelayCommand(Cancel);
            ExtractMetadataCommand = new RelayCommand(async () => await ExtractMetadataAsync());
        }

        public CreateExtensionVersionViewModel(MetroWindow window, string userToken, FbExtensionResult extension) : this()
        {
            _userToken = userToken;
            _extension = extension;
            _window = window;
        }

        private async Task CreateAsync()
        {
            var response = await _flowBloxWebApiService.Value.CreateExtensionVersionAsync(_userToken, new FbCreateExtensionVersionRequest
            {
                Version = _version,
                ExtensionGuid = _extension.Guid,
            });

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

                DetectedAssemblyName = metadata.AssemblyName;
                DetectedDescription = metadata.Description;
                Version = LibraryMetadataExtractor.NormalizeToMajorMinorPatch(metadata.Version);
            }
            catch (Exception ex)
            {
                await MessageBoxHelper.ShowMessageBoxAsync(
                    _window,
                    MessageBoxType.Error,
                    ApiErrorMessageHelper.BuildErrorMessage(
                        FlowBloxResourceUtil.GetLocalizedString("Error_ExtractMetadataFailed", typeof(Resources.CreateExtensionVersionWindow)),
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