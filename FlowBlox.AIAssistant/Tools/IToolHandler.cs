using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools
{
    public interface IToolHandler
    {
        AssistantScope Scope { get; }
        string Name { get; }
        ToolDefinition Definition { get; }
        bool IsLayoutRelevantForAutoAdjustment { get; }
        Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct);
    }
}