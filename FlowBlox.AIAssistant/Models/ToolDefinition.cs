using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Models
{
    public class ToolDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public bool IncludeContractInBootstrap { get; set; } = true;
        public JObject ArgumentsSchema { get; set; } = new JObject();
    }
}
