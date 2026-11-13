using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Runtime;

namespace FlowBloxTest.Runtime;

public sealed class ExecutionPrerequisiteProbeFlowBlock : FormatFlowBlock
{
    public int Count { get; set; } = 7;
    public ExecutionPrerequisiteProbeManagedObject? Dependency { get; set; }
    public ExecutionPrerequisiteProbeManagedObject? OtherDependency { get; set; }
    public bool RequirementsMet { get; set; } = true;
    public Action<ExecutionPrerequisiteProbeFlowBlock>? ObservePrerequisites { get; set; }
    public Action? ObserveExecution { get; set; }

    protected override bool ValidateRequirements(List<string> messages)
    {
        if (!RequirementsMet)
            messages.Add("Probe requirements not met.");

        return base.ValidateRequirements(messages);
    }

    public override void ValidateExecutionPrerequisites()
    {
        ObservePrerequisites?.Invoke(this);
    }

    public override bool Execute(BaseRuntime runtime, object data)
    {
        return Invoke(runtime, data, () =>
        {
            ObserveExecution?.Invoke();
            GenerateResult(runtime, "executed");
        });
    }
}
