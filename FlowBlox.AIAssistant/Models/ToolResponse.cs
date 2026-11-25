using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Models
{
    public class ToolResponse
    {
        [Newtonsoft.Json.JsonIgnore]
        public List<AssistantRetainedContent> RetainedContents { get; set; } = new();

        // Pure metadata rendering only: never re-executes a tool or increments retrieval counters.
        [Newtonsoft.Json.JsonIgnore]
        internal Action? RefreshBeforeSerialization { get; set; }
        internal void RefreshForSerialization() => RefreshBeforeSerialization?.Invoke();

        public bool Ok { get; set; }
        public JObject Result { get; set; } = new JObject();
        public string Error { get; set; } = string.Empty;
        public JArray Log { get; set; } = new JArray();
        public bool IsLayoutRelevantForAutoAdjustment { get; set; }
    }
}
