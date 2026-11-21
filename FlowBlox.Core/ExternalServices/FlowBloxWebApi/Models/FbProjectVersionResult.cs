using Newtonsoft.Json;

namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models
{
    public class FbProjectVersionResult
    {
        public long DownloadCount { get; set; }

        public int VersionNumber { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string Comment { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool? HasContent { get; set; }
    }
}
