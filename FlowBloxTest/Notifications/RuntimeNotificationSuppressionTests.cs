using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Interceptors;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Notifications;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using FlowBlox.Core.Services.Notifications;
using FlowBlox.Core.Util;
using Newtonsoft.Json;
using NSubstitute;

namespace FlowBloxTest.Notifications;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class RuntimeNotificationSuppressionTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RunnerFlag_ControlsAppNotificationsAcrossTheRuntimeLifecycle(bool enabled)
    {
        var options = FlowBloxOptions.GetOptionInstance();
        options.InitDefaults(false);
        var option = options.GetOption(OptionsConstants.NotificationsConfiguration);
        var previous = option.PersistentValue;
        var configuration = new RuntimeNotificationConfiguration { DoNotSendWhileDebugging = false };
        foreach (var rule in configuration.Notifications)
            rule.Enabled = true;

        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var start = project.FlowBloxRegistry.CreateFlowBlockUnregistered<StartFlowBlock>();
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(start);
        project.FlowBloxRegistry.Register(start);
        // Exercise the same request serialization used by isolated runners.
        var request = System.Text.Json.JsonSerializer.Deserialize<RunnerRequest>(RunnerJson.Serialize(new RunnerRequest
        {
            EnableAppNotifications = enabled
        }));
        using var runtime = new TransientRuntime(project) { EnableAppNotifications = request.EnableAppNotifications };
        var provider = Substitute.For<IRuntimeNotificationProvider>();
        var interceptor = new RuntimeNotificationInterceptor([provider]) { Runtime = runtime, Project = project };
        try
        {
            option.PersistentValue = JsonConvert.SerializeObject(configuration);
            interceptor.NotifyBeforeRuntimeStarted();
            interceptor.NotifyRuntimeStarted();
            interceptor.NotifyError(start, "Test failure", new InvalidOperationException("Test exception"));
            interceptor.NotifyRuntimeAborted(new InvalidOperationException("Test abort"));
            interceptor.NotifyRuntimeFinished();

            if (enabled)
            {
                provider.Received(3).Send(Arg.Any<RuntimeNotificationConfiguration>(), Arg.Any<RuntimeNotificationContext>());
            }
            else
            {
                provider.DidNotReceiveWithAnyArgs().Send(default!, default!);
            }
        }
        finally
        {
            option.PersistentValue = previous;
        }
    }
}
