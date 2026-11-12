using FlowBlox.Core.Enums;
using FlowBlox.Core.Interceptors;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime.Debugging;
using FlowBlox.Core.Provider;
using FlowBlox.Test.Runtime;

namespace FlowBloxTest.Runtime;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class RuntimeDebugReportTests
{
    [TestMethod]
    [DataRow(FlowBloxLogLevel.Warning)]
    [DataRow(FlowBloxLogLevel.Error)]
    public void Reports_CaptureProblemsAndExceptionDetailsWithoutInfoNoise(FlowBloxLogLevel level)
    {
        var runtime = CreateRuntime();
        var debugger = runtime.Interceptors.OfType<RuntimeDebuggingInterceptor>().Single();
        debugger.NotifyBeforeRuntimeStarted();
        var exception = new InvalidOperationException("Diagnostic details");

        runtime.Report("Info noise");
        runtime.Report("Repeated problem", level, exception);
        runtime.Report("Repeated problem", level, exception);
        debugger.NotifyRuntimeFinished();

        var result = runtime.GetDebuggingResult();
        var problems = level == FlowBloxLogLevel.Warning ? result.Warnings : result.Errors;
        Assert.HasCount(2, problems);
        Assert.IsTrue(problems.All(problem => problem.Exception == exception.ToString()));
        Assert.HasCount(2, result.Protocol.Where(entry => entry.EventType == level.ToString()).ToList());
        Assert.IsFalse(result.Protocol.Any(entry => entry.Message.Contains("Info noise")));
        Assert.IsFalse(runtime.Pause);
        Assert.IsFalse(runtime.Aborted);
    }

    [TestMethod]
    [DataRow(FlowBloxLogLevel.Warning)]
    [DataRow(FlowBloxLogLevel.Error)]
    public async Task ReportsAndNotifications_CountIndependentlyAcrossParallelInvocations(FlowBloxLogLevel level)
    {
        var runtime = CreateRuntime();
        var debugger = runtime.Interceptors.OfType<RuntimeDebuggingInterceptor>().Single();
        debugger.NotifyBeforeRuntimeStarted();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
        {
            var block = new StartFlowBlock { Name = "Block" + index };
            for (var invocation = 0; invocation < 2; invocation++)
            {
                runtime.Report("Same notification", level);
                if (level == FlowBloxLogLevel.Warning)
                    runtime.NotifyWarning(block, "Same notification");
                else
                    runtime.NotifyError(block, "Same notification");
            }
        })));
        debugger.NotifyRuntimeFinished();

        var result = runtime.GetDebuggingResult();
        var problems = level == FlowBloxLogLevel.Warning ? result.Warnings : result.Errors;
        Assert.HasCount(16, problems);
        var notifications = level == FlowBloxLogLevel.Warning ? result.WarningNotifications : result.ErrorNotifications;
        Assert.HasCount(16, notifications);
        Assert.IsTrue(notifications.GroupBy(problem => problem.FlowBlockName).All(group => group.Count() == 2));
        var protocol = result.Protocol.Where(entry => entry.EventType == level.ToString()).ToList();
        Assert.HasCount(16, protocol);
        var notificationProtocol = result.Protocol.Where(entry => entry.EventType == level + "Notification").ToList();
        Assert.HasCount(16, notificationProtocol);
        Assert.IsTrue(notificationProtocol.All(entry => entry.FlowBlockName.StartsWith("Block")));
    }

    [TestMethod]
    public void NotificationsWithoutReports_CountEveryOccurrenceSeparately()
    {
        var runtime = CreateRuntime();
        var debugger = runtime.Interceptors.OfType<RuntimeDebuggingInterceptor>().Single();
        debugger.NotifyBeforeRuntimeStarted();

        for (var invocation = 0; invocation < 5; invocation++)
            runtime.NotifyWarning(new StartFlowBlock { Name = "Source" }, "Warning");
        debugger.NotifyRuntimeFinished();

        Assert.IsEmpty(runtime.GetDebuggingResult().Warnings);
        Assert.HasCount(5, runtime.GetDebuggingResult().WarningNotifications);
        Assert.IsTrue(runtime.GetDebuggingResult().WarningNotifications.All(notification => notification.FlowBlockName == "Source"));
    }

    [TestMethod]
    public void Invocation_CapturesUnresolvedFieldReportsAfterValidationWithoutPausing()
    {
        var runtime = CreateRuntime();
        runtime.StopOnWarning = true;
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(runtime.Project.FlowBloxRegistry);
        var field = runtime.Project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Input");
        field.StringValue = "$Missing::Value";
        var block = new UnresolvedFieldProbeFlowBlock { Name = "Probe", Text = field.FullyQualifiedName };
        var debugger = runtime.Interceptors.OfType<RuntimeDebuggingInterceptor>().Single();
        debugger.NotifyBeforeRuntimeStarted();

        for (var invocation = 0; invocation < 5; invocation++)
            Assert.IsTrue(block.Execute(runtime, null!));
        debugger.NotifyRuntimeFinished();

        var result = runtime.GetDebuggingResult();
        Assert.HasCount(5, result.Warnings);
        Assert.IsTrue(result.Warnings.All(warning => warning.Message.Contains("$Missing::Value")));
        Assert.HasCount(5, result.Protocol.Where(entry => entry.EventType == "Warning").ToList());
        Assert.IsEmpty(result.WarningNotifications);
        Assert.IsFalse(runtime.Pause);
        Assert.IsFalse(runtime.Aborted);
    }

    [TestMethod]
    public void RuntimeLifecycle_DetachesReportCaptureAndStartsWithFreshResults()
    {
        var runtime = CreateRuntime();
        var debugger = runtime.Interceptors.OfType<RuntimeDebuggingInterceptor>().Single();
        debugger.NotifyBeforeRuntimeStarted();
        runtime.Report("First", FlowBloxLogLevel.Warning);
        debugger.NotifyRuntimeFinished();
        var firstResult = runtime.GetDebuggingResult();

        runtime.Report("After finish", FlowBloxLogLevel.Warning);
        debugger.NotifyBeforeRuntimeStarted();
        runtime.Report("Second", FlowBloxLogLevel.Warning);
        debugger.NotifyRuntimeFinished();

        Assert.HasCount(1, firstResult.Warnings);
        Assert.HasCount(1, runtime.GetDebuggingResult().Warnings);
        Assert.AreEqual("Second", runtime.GetDebuggingResult().Warnings.Single().Message);
    }

    private static FlowBloxUnitTestRuntime CreateRuntime()
    {
        var project = new FlowBloxProject();
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        return new FlowBloxUnitTestRuntime(project)
        {
            ExternalDebuggingInformation = new RuntimeExternalDebuggingInformation()
        };
    }
}
