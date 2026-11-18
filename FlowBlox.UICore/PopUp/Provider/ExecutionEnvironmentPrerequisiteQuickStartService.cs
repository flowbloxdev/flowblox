using System.Runtime.CompilerServices;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using Texts = FlowBlox.UICore.Resources.ClassResources.ExecutionEnvironmentPrerequisiteQuickStartService;

namespace FlowBlox.UICore.PopUp.Provider;

/// <summary>
/// Offers registered execution-environment setup guidance once per provider and execution, independently of onboarding state.
/// </summary>
public sealed class ExecutionEnvironmentPrerequisiteQuickStartService
{
    private readonly IReadOnlyList<IComponentPopupProvider> _providers;
    private readonly IExecutionEnvironmentPrerequisiteQuickStartHost _host;
    private readonly IFlowBloxMessageBoxService _messages;
    private readonly ConditionalWeakTable<object, HashSet<string>> _requested = new();
    private readonly Queue<(IComponentPopupProvider Provider, string ComponentName)> _pending = new();
    private bool _showing;

    public ExecutionEnvironmentPrerequisiteQuickStartService(
        IEnumerable<IComponentPopupProvider> providers,
        IExecutionEnvironmentPrerequisiteQuickStartHost host,
        IFlowBloxMessageBoxService messages)
    {
        _providers = providers.ToList();
        _host = host;
        _messages = messages;
    }

    public void RequestFor(object execution, IEnumerable<ExecutionEnvironmentPrerequisiteFailure> failures)
    {
        if (execution == null || failures == null)
            return;

        var requested = _requested.GetOrCreateValue(execution);
        foreach (var failure in failures)
        {
            if (failure == null)
                continue;

            // Match only registered providers. Runner data never causes assembly loading or target instantiation.
            var provider = _providers.FirstOrDefault(candidate =>
                candidate.IsQuickStart &&
                candidate.TargetType.FullName == failure.QuickStartTargetTypeName &&
                candidate.TargetType.Assembly.GetName().Name == failure.QuickStartTargetAssemblyName);
            if (provider == null)
                continue;

            lock (requested)
            {
                if (!requested.Add(provider.OptionKey))
                    continue;
            }

            _host.Post(() => Enqueue(provider, failure.ComponentName));
        }
    }

    private void Enqueue(IComponentPopupProvider provider, string componentName)
    {
        _pending.Enqueue((provider, componentName));
        if (_showing)
            return;

        _showing = true;
        try
        {
            while (_pending.TryDequeue(out var next))
            {
                try
                {
                    var component = string.IsNullOrWhiteSpace(next.ComponentName)
                        ? next.Provider.QuickStartTitle
                        : next.ComponentName;
                    var message = string.Format(
                        FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.SetupQuestion), typeof(Texts)),
                        component);
                    var title = FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.SetupTitle), typeof(Texts));
                    if (_messages.ShowMessageBox(message, title, FlowBloxMessageBoxTypes.Question) == FlowBloxMessageBoxDialogResult.Yes)
                        _host.Show(next.Provider);
                }
                catch (Exception exception)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        $"Failed to show execution-environment Quick Start for '{next.Provider.TargetType.FullName}'.", exception);
                }
            }
        }
        finally
        {
            _showing = false;
        }
    }
}
