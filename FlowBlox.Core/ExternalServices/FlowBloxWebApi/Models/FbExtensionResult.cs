using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models
{
    public class FbExtensionResult : INotifyPropertyChanged
    {
        private string _description;
        private bool _active;
        private bool _isDirty;

        public long DownloadCount { get; set; }
        public double? Relevance { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
                IsDirty = true;
            }
        }

        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                OnPropertyChanged();
                IsDirty = true;
            }
        }

        public List<FbVersionResult> Versions { get; set; }

        public FbExtensionResult()
        {
            Versions = new List<FbVersionResult>();
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                _isDirty = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
