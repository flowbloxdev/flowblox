using FlowBlox.AIAssistant.Builder;
using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace FlowBlox.AIAssistant.Models
{
    public sealed class AiAssistantHistoryDocument
    {
        private string? _legacyProjectJsonHash;

        public Guid HistoryGuid { get; set; } = Guid.NewGuid();
        public Guid ProjectGuid { get; set; }
        public string ComponentScope { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        public string LastContextJsonHash { get; set; } = string.Empty;
        public string ConversationSummary { get; set; } = string.Empty;
        public int SummarizedMessageCount { get; set; }
        public int CurrentContextTokens { get; set; }
        public int? EffectiveContextLimit { get; set; }
        public AssistantSessionUsage Usage { get; set; } = new();
        public List<string> LoadedExplanationKeys { get; set; } = new();
        public bool ShouldSerializeLoadedExplanationKeys() => false;
        public List<AssistantSessionMessage> SessionMessages { get; set; } = new();
        public List<AssistantTranscriptLine> Transcripts { get; set; } = new();

        [JsonProperty("LastProjectJsonHash")]
        private string? LegacyProjectJsonHash
        {
            set => _legacyProjectJsonHash = value;
        }

        [OnDeserialized]
        private void MigrateLegacyContextJsonHash(StreamingContext context)
        {
            if (string.IsNullOrWhiteSpace(LastContextJsonHash))
                LastContextJsonHash = _legacyProjectJsonHash ?? string.Empty;

            _legacyProjectJsonHash = null;
        }
    }
}
