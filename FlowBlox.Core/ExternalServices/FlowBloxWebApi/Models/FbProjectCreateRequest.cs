namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models
{
    public class FbProjectCreateRequest
    {
        public FbProjectClassification Classification { get; set; } = FbProjectClassification.Solution;
        public string Name { get; set; }
        public string Description { get; set; }
        public FbProjectVisibility Visibility { get; set; } = FbProjectVisibility.Private;
        public string ContentBase64 { get; set; }
    }

}
