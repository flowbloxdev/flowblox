using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal abstract class ToolHandlerBase : IToolHandler
    {
        public virtual AssistantScope Scope => AssistantScope.All;
        public abstract string Name { get; }
        public abstract ToolDefinition Definition { get; }
        public virtual bool IsLayoutRelevantForAutoAdjustment => false;
        public abstract Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct);
    }
}