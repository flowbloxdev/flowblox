using FlowBlox.Core.Exceptions;

namespace FlowBlox.Core.Models.Runtime;

/// <summary>
/// Carries setup guidance across process boundaries without serializing exceptions or loading target assemblies.
/// </summary>
public sealed class ExecutionEnvironmentPrerequisiteFailure
{
    public string ComponentName { get; set; } = string.Empty;
    public string QuickStartTargetTypeName { get; set; } = string.Empty;
    public string QuickStartTargetAssemblyName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Preserves execution-environment failures inside wrapped or aggregate exceptions.
    /// </summary>
    public static IEnumerable<ExecutionEnvironmentPrerequisiteFailure> FromException(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        if (exception != null)
            pending.Push(exception);

        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
                continue;

            if (current is ExecutionEnvironmentPrerequisiteException failure)
            {
                yield return new ExecutionEnvironmentPrerequisiteFailure
                {
                    ComponentName = failure.ComponentName,
                    QuickStartTargetTypeName = failure.QuickStartTargetType.FullName,
                    QuickStartTargetAssemblyName = failure.QuickStartTargetType.Assembly.GetName().Name,
                    Location = failure.Location,
                    Message = failure.Message
                };
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.Reverse())
                    pending.Push(inner);
            }
            else if (current.InnerException != null)
            {
                pending.Push(current.InnerException);
            }
        }
    }
}
