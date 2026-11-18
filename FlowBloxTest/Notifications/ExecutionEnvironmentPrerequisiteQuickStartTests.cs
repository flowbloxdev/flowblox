using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.FlowBlocks.Media;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.PopUp.Provider;
using NSubstitute;

namespace FlowBloxTest.Notifications;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ExecutionEnvironmentPrerequisiteQuickStartTests
{
    [TestMethod]
    public void Report_WrappedPrerequisiteFailure_DispatchesMatchingQuickStartOncePerExecution()
    {
        var project = new FlowBloxProject();
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        using var runtime = new TransientRuntime(project);
        var provider = CreateProvider(typeof(OnnxGenAIFlowBlock));
        var host = Substitute.For<IExecutionEnvironmentPrerequisiteQuickStartHost>();
        var posted = new Queue<Action>();
        host.When(value => value.Post(Arg.Any<Action>()))
            .Do(call => posted.Enqueue(call.Arg<Action>()));
        var messages = CreateAcceptingMessages();
        var service = new ExecutionEnvironmentPrerequisiteQuickStartService([provider], host, messages);
        runtime.ExecutionEnvironmentPrerequisiteFailed += (execution, failure) => service.RequestFor(execution, [failure]);
        var failure = new ExecutionEnvironmentPrerequisiteException("ONNX GenAI", typeof(OnnxGenAIFlowBlock), "missing-model");
        var wrapped = new AggregateException(new InvalidOperationException("Initialization failed", failure), failure);

        runtime.Report("Setup required", e: wrapped);
        runtime.Report("Setup required again", e: failure);

        host.DidNotReceive().Show(Arg.Any<IComponentPopupProvider>());
        Assert.HasCount(1, posted);
        posted.Dequeue()();
        host.Received(1).Show(provider);
        messages.Received(1).ShowMessageBox(
            Arg.Is<string>(message => message.Contains("ONNX GenAI")),
            Arg.Any<string>(),
            FlowBloxMessageBoxTypes.Question);

        service.RequestFor(new object(), ExecutionEnvironmentPrerequisiteFailure.FromException(failure));
        posted.Dequeue()();
        host.Received(2).Show(provider);
    }

    [TestMethod]
    public void RequestFor_UnregisteredOrOrdinaryFailure_DoesNotOpenGuidance()
    {
        var provider = CreateProvider(typeof(OnnxGenAIFlowBlock));
        var host = Substitute.For<IExecutionEnvironmentPrerequisiteQuickStartHost>();
        var messages = CreateAcceptingMessages();
        var service = new ExecutionEnvironmentPrerequisiteQuickStartService([provider], host, messages);

        service.RequestFor(new object(), ExecutionEnvironmentPrerequisiteFailure.FromException(new InvalidOperationException("Other failure")));
        service.RequestFor(new object(), ExecutionEnvironmentPrerequisiteFailure.FromException(
            new ExecutionEnvironmentPrerequisiteException("yt-dlp", typeof(YtDlpMediaDownloadProvider), "missing-tool")));

        host.DidNotReceive().Post(Arg.Any<Action>());
        messages.DidNotReceive().ShowMessageBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FlowBloxMessageBoxTypes>());
    }

    [TestMethod]
    public void RequestFor_ReentrantFailures_QueuesGuidanceWithoutStackingDialogs()
    {
        var first = CreateProvider(typeof(OnnxGenAIFlowBlock));
        var second = CreateProvider(typeof(YtDlpMediaDownloadProvider));
        var host = Substitute.For<IExecutionEnvironmentPrerequisiteQuickStartHost>();
        host.When(value => value.Post(Arg.Any<Action>())).Do(call => call.Arg<Action>()());
        var service = new ExecutionEnvironmentPrerequisiteQuickStartService([first, second], host, CreateAcceptingMessages());
        var execution = new object();
        var sequence = new List<string>();
        host.When(value => value.Show(first)).Do(_ =>
        {
            sequence.Add("first opened");
            service.RequestFor(execution, ExecutionEnvironmentPrerequisiteFailure.FromException(
                new ExecutionEnvironmentPrerequisiteException("yt-dlp", typeof(YtDlpMediaDownloadProvider), "missing-tool")));
            sequence.Add("first closed");
        });
        host.When(value => value.Show(second)).Do(_ => sequence.Add("second opened"));

        service.RequestFor(execution, ExecutionEnvironmentPrerequisiteFailure.FromException(
            new ExecutionEnvironmentPrerequisiteException("ONNX GenAI", typeof(OnnxGenAIFlowBlock), "missing-model")));

        CollectionAssert.AreEqual(new[] { "first opened", "first closed", "second opened" }, sequence);
    }

    [TestMethod]
    public void RequestFor_DeclinedGuidance_DoesNotOpenOrAskAgainWithinExecution()
    {
        var provider = CreateProvider(typeof(OnnxGenAIFlowBlock));
        var host = Substitute.For<IExecutionEnvironmentPrerequisiteQuickStartHost>();
        host.When(value => value.Post(Arg.Any<Action>())).Do(call => call.Arg<Action>()());
        var messages = Substitute.For<IFlowBloxMessageBoxService>();
        messages.ShowMessageBox(Arg.Any<string>(), Arg.Any<string>(), FlowBloxMessageBoxTypes.Question)
            .Returns(FlowBloxMessageBoxDialogResult.No);
        var service = new ExecutionEnvironmentPrerequisiteQuickStartService([provider], host, messages);
        var execution = new object();
        var failures = ExecutionEnvironmentPrerequisiteFailure.FromException(
            new ExecutionEnvironmentPrerequisiteException("ONNX GenAI", typeof(OnnxGenAIFlowBlock), "missing-model")).ToList();

        service.RequestFor(execution, failures);
        service.RequestFor(execution, failures);

        host.DidNotReceive().Show(Arg.Any<IComponentPopupProvider>());
        messages.Received(1).ShowMessageBox(Arg.Any<string>(), Arg.Any<string>(), FlowBloxMessageBoxTypes.Question);

        service.RequestFor(new object(), failures);

        messages.Received(2).ShowMessageBox(Arg.Any<string>(), Arg.Any<string>(), FlowBloxMessageBoxTypes.Question);
    }

    private static IFlowBloxMessageBoxService CreateAcceptingMessages()
    {
        var messages = Substitute.For<IFlowBloxMessageBoxService>();
        messages.ShowMessageBox(Arg.Any<string>(), Arg.Any<string>(), FlowBloxMessageBoxTypes.Question)
            .Returns(FlowBloxMessageBoxDialogResult.Yes);
        return messages;
    }

    private static IComponentPopupProvider CreateProvider(Type type)
    {
        var provider = Substitute.For<IComponentPopupProvider>();
        provider.TargetType.Returns(type);
        provider.OptionKey.Returns(type.FullName);
        provider.IsQuickStart.Returns(true);
        return provider;
    }
}
