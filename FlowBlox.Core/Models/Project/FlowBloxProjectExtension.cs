using FlowBlox.Core.Constants;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.Util;
using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace FlowBlox.Core.Models.Project
{
    [Serializable()]
    public class FlowBloxProjectExtensionDependency
    {
        public string EndpointUri { get; set; }

        public string ExtensionName { get; set; }

        public Guid ExtensionGuid { get; set; }

        public string Version { get; set; }

        public FlowBloxProjectExtensionDependency(string extensionName, Guid extensionGuid, string version, string endpointUri)
        {
            ExtensionName = extensionName;
            ExtensionGuid = extensionGuid;
            Version = version;
            EndpointUri = endpointUri;
        }
    }

    [Serializable()]
    public class FlowBloxProjectExtension : INotifyPropertyChanged
    {
        private string _endpointUri;
        private string _name;
        private string _version;
        private string _offlineExtensionPath;
        private bool _debugMode;
        private Guid? _guid;
        private List<FlowBloxProjectExtensionDependency> _dependencies;

        public bool Online => _guid != null;

        public string EndpointUri
        {
            get => _endpointUri;
            set
            {
                if (_endpointUri != value)
                {
                    _endpointUri = value;
                    _flowBloxWebApiService = new Lazy<FlowBloxWebApiService>(() => new FlowBloxWebApiService(_endpointUri));
                    OnPropertyChanged();
                }
            }
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public Guid? ExtensionGuid
        {
            get => _guid;
            set
            {
                if (_guid != value)
                {
                    _guid = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Version
        {
            get => _version;
            set
            {
                if (_version != value)
                {
                    _version = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<FlowBloxProjectExtensionDependency> Dependencies
        {
            get => _dependencies;
            set
            {
                if (_dependencies != value)
                {
                    _dependencies = value;
                    OnPropertyChanged();
                }
            }
        }

        public string OfflineExtensionPath
        {
            get => _offlineExtensionPath;
            set
            {
                if (_offlineExtensionPath != value)
                {
                    _offlineExtensionPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool DebugMode
        {
            get => _debugMode;
            set
            {
                if (_debugMode != value)
                {
                    _debugMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LocalExtensionDirectory));
                }
            }
        }

        public string LocalExtensionDirectory
        {
            get
            {
                var extensionsDirOption = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsExtensionsDir);
                var extensionsDir = extensionsDirOption.Value;
                var targetDirectory = Path.Combine(extensionsDir, Name, Version);

                if (Online)
                {
                    if (!Directory.Exists(targetDirectory) ||
                        !Directory.GetFiles(targetDirectory).Any(x => !x.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    {
                        Directory.CreateDirectory(targetDirectory);
                        Task.Run(async () => await DownloadVersionContentAsync(targetDirectory)).GetAwaiter().GetResult();
                    }

                    return targetDirectory;
                }
                else
                {
                    if (!DebugMode)
                    {
                        var offlineExtensionDirectory = Path.GetDirectoryName(OfflineExtensionPath);

                        if (!Directory.Exists(targetDirectory) ||
                            !File.Exists(Path.Combine(targetDirectory, $"{Name}.dll")) ||
                            Version != System.Diagnostics.FileVersionInfo.GetVersionInfo(OfflineExtensionPath).ProductVersion ||
                            !CompareExtensionDirectories(offlineExtensionDirectory, targetDirectory))
                        {
                            if (Directory.Exists(targetDirectory))
                                Directory.Delete(targetDirectory, true);

                            IOUtil.CopyDirectory(offlineExtensionDirectory, targetDirectory);

                            Version = System.Diagnostics.FileVersionInfo.GetVersionInfo(OfflineExtensionPath).ProductVersion;
                            OnPropertyChanged(nameof(Version));
                        }

                        return targetDirectory;
                    }
                    else
                    {
                        return Path.GetDirectoryName(OfflineExtensionPath);
                    }
                }
            }
        }

        private bool CompareExtensionDirectories(string? offlineExtensionDirectory, string targetDirectory)
        {
            if (string.IsNullOrEmpty(offlineExtensionDirectory) || !Directory.Exists(offlineExtensionDirectory))
                return false;

            if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
                return false;

            var offlineFiles = IOUtil.GetFilesInDirectoryRecursive(offlineExtensionDirectory, "*.dll");
            var targetFiles = IOUtil.GetFilesInDirectoryRecursive(targetDirectory, "*.dll");

            if (!offlineFiles.SequenceEqual(targetFiles, StringComparer.OrdinalIgnoreCase))
                return false;

            foreach (var relativePath in offlineFiles)
            {
                var offlineBytes = File.ReadAllBytes(Path.Combine(offlineExtensionDirectory, relativePath));
                var targetBytes = File.ReadAllBytes(Path.Combine(targetDirectory, relativePath));

                var offlineHash = HashHelper.ComputeSHA256Hash(offlineBytes);
                var targetHash = HashHelper.ComputeSHA256Hash(targetBytes);

                if (!string.Equals(offlineHash, targetHash, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private Lazy<FlowBloxWebApiService> _flowBloxWebApiService;

        public FlowBloxProjectExtension()
        {
            _flowBloxWebApiService = new Lazy<FlowBloxWebApiService>(() => new FlowBloxWebApiService(_endpointUri));
        }

        private async Task DownloadVersionContentAsync(string targetDirectory)
        {
            if (!ExtensionGuid.HasValue)
                return;

            var resp = await _flowBloxWebApiService.Value.GetVersionContentAsync(ExtensionGuid.Value, Version);

            if (!resp.Success)
                throw new InvalidOperationException($"The version content of extension \"{Name}\" ({ExtensionGuid}) and version \"{Version}\" could not be retrieved from the server. {resp.ErrorMessage}");

            string contentBase64 = resp.ResultObject;
            if (!string.IsNullOrEmpty(contentBase64))
            {
                byte[] fileContent = Convert.FromBase64String(contentBase64);
                string zipFilePath = Path.Combine(targetDirectory, $"{Name}_{Version}.zip");

                File.WriteAllBytes(zipFilePath, fileContent);

                ZipFile.ExtractToDirectory(zipFilePath, targetDirectory);
                File.Delete(zipFilePath);

                OnPropertyChanged(nameof(LocalExtensionDirectory));
            }
            else
            {
                throw new InvalidOperationException($"The server returned empty version content for extension \"{Name}\" ({ExtensionGuid}) and version \"{Version}\".");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}