using FlowBlox.Core.Models.Base;

namespace FlowBloxTest.Runtime;

public sealed class ExecutionEnvironmentPrerequisiteProbeManagedObject : ManagedObject
{
    private int _validationCount;
    public int ValidationCount => _validationCount;
    public ExecutionEnvironmentPrerequisiteProbeManagedObject? Child { get; set; }
    public Action? ObservePrerequisites { get; set; }

    public override void ValidateExecutionEnvironmentPrerequisites()
    {
        _validationCount++;
        ObservePrerequisites?.Invoke();
    }
}
