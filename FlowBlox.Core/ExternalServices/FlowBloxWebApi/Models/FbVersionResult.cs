using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models
{
    public class FbVersionResult : INotifyPropertyChanged
    {
        private string _changes;
        private string _runtimeVersion;
        private bool _active;
        private bool _isDirty;
        private bool __backwardsCompatible;
        private string _archivePath;
        private byte[] _archiveContent;
        private List<FbVersionDependency> _dependencies;

        public long DownloadCount { get; set; }
        public DateTime? CreatedAt { get; set; }

        public FbVersionResult()
        {

        }

        public string RuntimeVersion
        {
            get => _runtimeVersion;
            set
            {
                if (_runtimeVersion != value)
                {
                    _runtimeVersion = value;
                    OnPropertyChanged();
                    IsDirty = true;
                }
            }
        }

        public string Version { get; set; }

        public bool BackwardsCompatible
        {
            get => __backwardsCompatible;
            set
            {
                if (__backwardsCompatible != value)
                {
                    __backwardsCompatible = value;
                    OnPropertyChanged();
                    IsDirty = true;
                }
            }
        }

        public bool Active
        {
            get => _active;
            set
            {
                if (_active != value)
                {
                    _active = value;
                    OnPropertyChanged();
                    IsDirty = true;
                }
            }
        }

        public bool Released { get; set; }

        public string Changes
        {
            get => _changes;
            set
            {
                if (_changes != value)
                {
                    _changes = value;
                    OnPropertyChanged();
                    IsDirty = true;
                }   
            }
        }

        public List<FbVersionDependency> Dependencies
        {
            get => _dependencies;
            set
            {
                if (_dependencies != value)
                {
                    _dependencies = value;
                    OnPropertyChanged();
                    IsDirty = true;
                }
            }
        }

        [JsonIgnore()]
        public string ArchivePath
        {
            get => _archivePath;
            set
            {
                if (_archivePath != value)
                {
                    _archivePath = value;
                    OnPropertyChanged();

                    if (_archivePath != null)
                        IsDirty = true;

                    if (!string.IsNullOrEmpty(_archivePath))
                        ArchiveContent = File.ReadAllBytes(_archivePath);
                    else
                        ArchiveContent = null;
                }
            }
        }

        [JsonIgnore()]
        public byte[] ArchiveContent
        {
            get => _archiveContent;
            private set
            {
                if (_archiveContent != value)
                {
                    _archiveContent = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonIgnore()]
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
