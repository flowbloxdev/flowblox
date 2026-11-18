using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.UICore.PopUp.Provider;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    private void Runtime_ExecutionEnvironmentPrerequisiteFailed(BaseRuntime runtime, ExecutionEnvironmentPrerequisiteFailure failure)
    {
        FlowBloxServiceLocator.Instance.GetService<ExecutionEnvironmentPrerequisiteQuickStartService>()
            ?.RequestFor(runtime, [failure]);
    }

    private void Runtime_DetachExecutionEnvironmentPrerequisiteGuidance(object result)
    {
        if (result is BaseRuntime runtime)
        {
            runtime.ExecutionEnvironmentPrerequisiteFailed -= Runtime_ExecutionEnvironmentPrerequisiteFailed;
            runtime.Finish -= Runtime_DetachExecutionEnvironmentPrerequisiteGuidance;
        }
    }

    private void Execution_Completed(ProjectExecutionEntry entry)
    {
        FlowBloxServiceLocator.Instance.GetService<ExecutionEnvironmentPrerequisiteQuickStartService>()
            ?.RequestFor(entry, entry.Response?.ExecutionEnvironmentPrerequisiteFailures);
    }
}
