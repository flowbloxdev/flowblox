namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models
{
    public class FbProjectChangeRequest
    {
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public FbProjectClassification? Classification { get; set; }

        public string ProjectGuid { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public FbProjectVisibility? Visibility { get; set; }
        public string ContentBase64 { get; set; }
    }
}
