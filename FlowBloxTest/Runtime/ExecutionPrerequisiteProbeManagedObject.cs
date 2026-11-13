using FlowBlox.Core.Models.Base;

namespace FlowBloxTest.Runtime;

public sealed class ExecutionPrerequisiteProbeManagedObject : ManagedObject
{
    private int _validationCount;
    public int ValidationCount => _validationCount;
    public ExecutionPrerequisiteProbeManagedObject? Child { get; set; }
    public Action? ObservePrerequisites { get; set; }

    public override void ValidateExecutionPrerequisites()
    {
        _validationCount++;
        ObservePrerequisites?.Invoke();
    }
}
