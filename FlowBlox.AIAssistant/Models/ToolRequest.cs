using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Models
{
    public class ToolRequest
    {
        public string ToolName { get; set; } = string.Empty;
        public JObject Arguments { get; set; } = new JObject();
        public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
        public IReadOnlyDictionary<string, string>? ConfirmationInputParameters { get; internal set; }
        public bool RememberProjectApproval { get; set; }
    }
}
