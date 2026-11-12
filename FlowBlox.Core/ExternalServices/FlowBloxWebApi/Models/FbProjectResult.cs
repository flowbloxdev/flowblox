namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models
{
    public class FbProjectResult
    {
        public FbProjectResult()
        {
            Versions = new List<FbProjectVersionResult>();
        }

        public string Guid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public FbProjectVisibility Visibility { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public List<FbProjectVersionResult> Versions { get; set; }

    }
}
