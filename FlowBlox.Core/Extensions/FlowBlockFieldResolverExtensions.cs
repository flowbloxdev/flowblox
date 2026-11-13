using System.Linq.Expressions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;

namespace FlowBlox.Core.Extensions;

/// <summary>
/// Resolves configuration with explicit FlowBlock notification and completion behavior.
/// </summary>
public static class FlowBlockFieldResolverExtensions
{
    /// <summary>
    /// On an empty value, creates the notification and completes the flow contract:
    /// result blocks emit an empty result; other blocks execute their next blocks.
    /// The caller must return when this method returns false; optional values are handled by the caller.
    /// </summary>
    public static bool ResolveOrCreateNotification<TBlock, TValue>(
        this TBlock block,
        Expression<Func<TBlock, TValue>> property,
        out TValue value,
        BaseRuntime runtime,
        Enum notification)
        where TBlock : BaseFlowBlock
    {
        if (block.TryResolve(property, out value))
            return true;

        block.CreateNotification(runtime, notification);
        block.CompleteFailedExecution(runtime);
        return false;
    }
}
