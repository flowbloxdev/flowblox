using Newtonsoft.Json;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.AIAssistant.Models;

namespace FlowBlox.AIAssistant.Builder
{
    [JsonConverter(typeof(AssistantSessionMessageJsonConverter))]
    public abstract class AssistantSessionMessage
    {
        public abstract string Role { get; }
        public abstract string CompleteMessage { get; }
        public int CharacterCount => CompleteMessage?.Length ?? 0;
        public List<AssistantRetainedContent> RetainedContents { get; set; } = new();

        public abstract AssistantSessionMessage Clone();
    }

    public sealed class AssistantSingleMessage : AssistantSessionMessage
    {
        public string MessageRole { get; set; } = "user";
        public string Message { get; set; } = string.Empty;

        public override string Role => string.Equals(MessageRole, "assistant", StringComparison.OrdinalIgnoreCase)
            ? "assistant"
            : "user";

        public override string CompleteMessage => Message?.Trim() ?? string.Empty;

        public override AssistantSessionMessage Clone()
        {
            return new AssistantSingleMessage
            {
                MessageRole = Role,
                Message = Message,
                RetainedContents = RetainedContents.Select(content => content.Clone()).ToList()
            };
        }
    }

    public sealed class AssistantMessagePair : AssistantSessionMessage
    {
        public string AssistantRequest { get; set; } = string.Empty;
        public string ToolApiResponse { get; set; } = string.Empty;
        /// <summary>
        /// Context-only replacements indexed by tool result position. Original history text remains unchanged.
        /// </summary>
        public Dictionary<int, string> CompactedToolResults { get; set; } = new();
        public AIChatMessageMetadata Metadata { get; set; } = new();

        public override string Role => "user";

        public override string CompleteMessage
        {
            get
            {
                var assistantRequest = AssistantRequest?.Trim() ?? string.Empty;
                var toolApiResponse = ToolApiResponse?.Trim() ?? string.Empty;
                return
                    "Assistant request:\n" +
                    assistantRequest +
                    "\n\nTool API response:\n" +
                    toolApiResponse;
            }
        }

        public override AssistantSessionMessage Clone()
        {
            return new AssistantMessagePair
            {
                AssistantRequest = AssistantRequest,
                ToolApiResponse = ToolApiResponse,
                CompactedToolResults = new Dictionary<int, string>(CompactedToolResults),
                Metadata = Metadata?.Clone() ?? new AIChatMessageMetadata(),
                RetainedContents = RetainedContents.Select(content => content.Clone()).ToList()
            };
        }
    }

    internal sealed class AssistantSessionMessageJsonConverter : JsonConverter<AssistantSessionMessage>
    {
        public override void WriteJson(JsonWriter writer, AssistantSessionMessage? value, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            switch (value)
            {
                case AssistantMessagePair pair:
                    writer.WritePropertyName("messageType");
                    writer.WriteValue("pair");
                    writer.WritePropertyName(nameof(AssistantMessagePair.AssistantRequest));
                    writer.WriteValue(pair.AssistantRequest);
                    writer.WritePropertyName(nameof(AssistantMessagePair.ToolApiResponse));
                    writer.WriteValue(pair.ToolApiResponse);
                    if (pair.CompactedToolResults.Count > 0)
                    {
                        writer.WritePropertyName(nameof(AssistantMessagePair.CompactedToolResults));
                        serializer.Serialize(writer, pair.CompactedToolResults);
                    }
                    if (pair.Metadata?.HasValues == true)
                    {
                        writer.WritePropertyName(nameof(AssistantMessagePair.Metadata));
                        serializer.Serialize(writer, pair.Metadata);
                    }
                    break;

                case AssistantSingleMessage single:
                    writer.WritePropertyName("messageType");
                    writer.WriteValue("single");
                    writer.WritePropertyName(nameof(AssistantSingleMessage.MessageRole));
                    writer.WriteValue(single.Role);
                    writer.WritePropertyName(nameof(AssistantSingleMessage.Message));
                    writer.WriteValue(single.Message);
                    break;

                case null:
                    break;

                default:
                    throw new JsonSerializationException($"Unsupported assistant session message type '{value.GetType().FullName}'.");
            }

            if (value?.RetainedContents.Count > 0)
            {
                writer.WritePropertyName(nameof(AssistantSessionMessage.RetainedContents));
                serializer.Serialize(writer, value.RetainedContents);
            }

            writer.WriteEndObject();
        }

        public override AssistantSessionMessage? ReadJson(
            JsonReader reader,
            Type objectType,
            AssistantSessionMessage? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
            var messageType = obj.Value<string>("messageType") ?? obj.Value<string>("MessageType") ?? "single";
            var retainedContent = (obj[nameof(AssistantSessionMessage.RetainedContents)] ?? obj["RetainOnSummarization"])?
                .ToObject<List<AssistantRetainedContent>>(serializer) ?? new();

            if (string.Equals(messageType, "pair", StringComparison.OrdinalIgnoreCase))
            {
                return new AssistantMessagePair
                {
                    AssistantRequest = obj.Value<string>(nameof(AssistantMessagePair.AssistantRequest)) ?? string.Empty,
                    ToolApiResponse = obj.Value<string>(nameof(AssistantMessagePair.ToolApiResponse)) ?? string.Empty,
                    CompactedToolResults = obj[nameof(AssistantMessagePair.CompactedToolResults)]?
                        .ToObject<Dictionary<int, string>>(serializer) ?? new(),
                    Metadata = obj[nameof(AssistantMessagePair.Metadata)]?.ToObject<AIChatMessageMetadata>(serializer)
                        ?? new AIChatMessageMetadata(),
                    RetainedContents = retainedContent
                };
            }

            return new AssistantSingleMessage
            {
                MessageRole = obj.Value<string>(nameof(AssistantSingleMessage.MessageRole))
                    ?? obj.Value<string>("Role")
                    ?? "user",
                Message = obj.Value<string>(nameof(AssistantSingleMessage.Message))
                    ?? obj.Value<string>("Content")
                    ?? string.Empty,
                RetainedContents = retainedContent
            };
        }
    }
}
