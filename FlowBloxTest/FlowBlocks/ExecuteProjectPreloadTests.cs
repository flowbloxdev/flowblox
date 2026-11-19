using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using Newtonsoft.Json;

namespace FlowBloxTest.FlowBlocks;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ExecuteProjectPreloadTests
{
    [TestMethod]
    public void RuntimeStart_RefreshesSuggestions_ThenKeepsMetadataUntilRuntimeFinished()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FlowBloxPreloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "child.fbprj");
            WriteProject(file, "Before");
            var parent = CreateParent();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(parent.FlowBloxRegistry);
            using var runtime = new TransientRuntime(parent);
            var block = new ExecuteProjectFlowBlock { Name = "Child", ProjectFile = file };
            CollectionAssert.AreEqual(new[] { "Before" }, block.GetInputFieldNames().ToArray());

            WriteProject(file, "AtStart");
            block.RuntimeStarted(runtime);
            CollectionAssert.AreEqual(new[] { "AtStart" }, block.GetInputFieldNames().ToArray());
            WriteProject(file, "AfterStart");
            CollectionAssert.AreEqual(new[] { "AtStart" }, block.GetInputFieldNames().ToArray());
            block.RuntimeFinished(runtime);
            CollectionAssert.AreEqual(new[] { "AfterStart" }, block.GetInputFieldNames().ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void MissingPreload_ThrowsBeforeExecution_AndReportsTheProjectReference()
    {
        var parent = CreateParent();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(parent.FlowBloxRegistry);
        using var runtime = new TransientRuntime(parent);
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.fbprj");
        var block = new ExecuteProjectFlowBlock { Name = "MissingChild", ProjectFile = file };
        var errors = new List<string>();
        runtime.LogMessageCreated += (_, message, level) =>
        {
            if (level == FlowBloxLogLevel.Error)
                errors.Add(message);
        };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => block.RuntimeStarted(runtime));
        StringAssert.Contains(exception.Message, file);
        Assert.IsTrue(errors.Any(message => message.Contains(file, StringComparison.Ordinal)));
    }

    private static FlowBloxProject CreateParent()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var start = project.FlowBloxRegistry.CreateFlowBlockUnregistered<StartFlowBlock>();
        project.FlowBloxRegistry.PostProcessFlowBlockCreated(start);
        project.FlowBloxRegistry.Register(start);
        return project;
    }

    private static void WriteProject(string file, string inputName)
    {
        File.WriteAllText(file, JsonConvert.SerializeObject(new
        {
            ProjectName = "Child",
            FlowBlocks = Array.Empty<object>(),
            ManagedObjects = Array.Empty<object>(),
            UserFields = new[] { new { Name = inputName, UserField = true, UserFieldType = "Input" } }
        }));
    }
}
