using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Execution;

namespace FlowBloxTest.Runtime;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
[DoNotParallelize]
public sealed class ExecutionEnvironmentClassificationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingModelConfiguration_DoesNotRequestEnvironmentSetup(bool genAi)
    {
        var project = new FlowBloxProject();
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        using var runtime = new TransientRuntime(project);
        BaseFlowBlock block = genAi
            ? new OnnxGenAIFlowBlock { ModelFolder = "" }
            : new OnnxQAFlowBlock { ModelFolder = "" };
        var failures = new List<ExecutionEnvironmentPrerequisiteFailure>();
        runtime.ExecutionEnvironmentPrerequisiteFailed += (_, failure) => failures.Add(failure);

        Assert.ThrowsExactly<InvalidOperationException>(() => block.RuntimeStarted(runtime));
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingModelDirectory_RequestsEnvironmentSetupWithoutLoadingNativeLibraries(bool genAi)
    {
        var project = new FlowBloxProject();
        project.FlowBloxRegistry.RegisterFlowBlock(new StartFlowBlock { Name = "Start" });
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        using var runtime = new TransientRuntime(project);
        var missingFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        BaseFlowBlock block = genAi
            ? new OnnxGenAIFlowBlock { ModelFolder = missingFolder }
            : new OnnxQAFlowBlock { ModelFolder = missingFolder };
        var failures = new List<ExecutionEnvironmentPrerequisiteFailure>();
        runtime.ExecutionEnvironmentPrerequisiteFailed += (_, failure) => failures.Add(failure);

        var exception = Assert.ThrowsExactly<ExecutionEnvironmentPrerequisiteException>(() => block.RuntimeStarted(runtime));

        Assert.AreEqual(missingFolder, exception.Location);
        Assert.HasCount(1, failures);
        Assert.AreEqual(block.GetType().FullName, failures[0].QuickStartTargetTypeName);
    }

    [TestMethod]
    public void MissingExecutableConfiguration_IsAnOrdinaryConfigurationError()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ExecutionEnvironmentPrerequisites.RequireExecutable<OnnxGenAIFlowBlock>("tool", ""));
    }

    [TestMethod]
    [DataRow(null, "{}")]
    [DataRow("ChromeWebBrowser", null)]
    [DataRow("UnknownBrowserImplementation", "{}")]
    [DataRow("ChromeWebBrowser", "{")]
    public void InvalidBrowserConfiguration_DoesNotBecomeAnEnvironmentFailure(string service, string settings)
    {
        var options = FlowBloxOptions.GetOptionInstance().OptionCollection;
        options.TryGetValue(OptionsConstants.WebBrowserServiceName, out var originalService);
        options.TryGetValue(OptionsConstants.WebBrowserServiceSettings, out var originalSettings);
        try
        {
            options[OptionsConstants.WebBrowserServiceName] = new OptionElement { Value = service };
            options[OptionsConstants.WebBrowserServiceSettings] = new OptionElement { Value = settings };
            var block = new WebBrowserFlowBlock();

            var exception = Assert.Throws<Exception>(() => _ = block.InternalWebBrowser);

            Assert.IsEmpty(ExecutionEnvironmentPrerequisiteFailure.FromException(exception).ToList());
        }
        finally
        {
            if (originalService == null)
                options.Remove(OptionsConstants.WebBrowserServiceName);
            else
                options[OptionsConstants.WebBrowserServiceName] = originalService;
            if (originalSettings == null)
                options.Remove(OptionsConstants.WebBrowserServiceSettings);
            else
                options[OptionsConstants.WebBrowserServiceSettings] = originalSettings;
        }
    }
}
