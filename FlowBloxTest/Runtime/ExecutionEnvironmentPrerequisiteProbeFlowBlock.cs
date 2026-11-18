using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Runtime;

namespace FlowBloxTest.Runtime;

public sealed class ExecutionEnvironmentPrerequisiteProbeFlowBlock : FormatFlowBlock
{
    public int Count { get; set; } = 7;
    public ExecutionEnvironmentPrerequisiteProbeManagedObject? Dependency { get; set; }
    public ExecutionEnvironmentPrerequisiteProbeManagedObject? OtherDependency { get; set; }
    public bool RequirementsMet { get; set; } = true;
    public Action<ExecutionEnvironmentPrerequisiteProbeFlowBlock>? ObservePrerequisites { get; set; }
    public Action? ObserveExecution { get; set; }

    protected override bool ValidateRequirements(List<string> messages)
    {
        if (!RequirementsMet)
            messages.Add("Probe requirements not met.");

        return base.ValidateRequirements(messages);
    }

    public override void ValidateExecutionEnvironmentPrerequisites()
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
