using FlowBlox.Core.Models.FlowBlocks.AIRemote;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Enums;

namespace FlowBloxTest.Runtime;

public sealed class FieldReferenceCapturingAIProvider : AIProviderBase
{
    public override string ProviderType => "FieldReferenceProbe";
    public AIChatRequest LastRequest { get; private set; } = null!;
    public int ResolvedTimeout { get; private set; }
    public AIReasoningEffort ResolvedReasoningEffort { get; private set; }

    public FieldReferenceCapturingAIProvider()
    {
        ApiKey = "test";
    }

    protected override Task<AIResponse> ExecuteChatCoreAsync(AIChatRequest request, CancellationToken ct)
    {
        LastRequest = request;
        ResolvedTimeout = ResolveTimeoutSeconds(request);
        ResolvedReasoningEffort = GetRuntimeReasoningEffort();
        return Task.FromResult(new AIResponse { Success = true, Text = "generated" });
    }
}
