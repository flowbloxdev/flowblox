using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.LocalAI;
using FlowBlox.UICore.Manager;
using FlowBlox.UICore.Operations;

namespace FlowBloxTest.LocalAI;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class LocalAISetupTransactionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SetupInPropertyTransaction_KeepsGlobalResourcesAndCommitsOnlyBlockChanges(bool commit)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var options = FlowBloxOptions.GetOptionInstance();
        options.InitDefaults(false);
        var protocolOption = options.GetOption(OptionsConstants.PathsDeepCopierProtocolDir);
        var previousProtocolDirectory = protocolOption.Value;
        protocolOption.Value = Path.Combine(Path.GetTempPath(), "FlowBloxLocalAITransactionTests");
        var transaction = new PropertyViewTransactionManager();
        try
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var original = new OnnxGenAIFlowBlock { Name = "SyntheticONNX", ModelFolder = "Before" };
            project.FlowBloxRegistry.PostProcessFlowBlockCreated(original);
            project.FlowBloxRegistry.RegisterFlowBlock(original);
            var opened = transaction.Open(original);
            var working = (OnnxGenAIFlowBlock)opened.TransientTarget;
            Assert.IsTrue(new OnnxFlowBlockUIActions(working).CanConfigureLocalAI());
            Assert.IsTrue(new OnnxFlowBlockUIActions(original).CanConfigureLocalAI());

            var setup = new LocalAISetup(project, original, LocalAIModel.Qwen3Instruct, _ => "synthetic script");
            setup.Prepare();
            setup.ConfigureProvider();
            setup.ConfigureInputFile();
            setup.ConfigureBlock();

            Assert.AreEqual("Before", original.ModelFolder);
            Assert.AreEqual(setup.ModelFolder, working.ModelFolder);
            Assert.AreEqual(AiExecutionProviders.Default, working.Resolve(block => block.AiExecutionProvider));
            Assert.AreSame(setup.ProviderField, opened.Registry.GetUserFields().Single());
            Assert.AreSame(setup.ProviderField, project.FlowBloxRegistry.GetUserFields().Single());
            setup.ProviderField.StringValue = "DirectML";
            Assert.AreEqual(AiExecutionProviders.DirectML, working.Resolve(block => block.AiExecutionProvider));

            if (commit)
                transaction.Commit(original, working);
            else
                transaction.Cancel();

            Assert.AreEqual(commit ? setup.ModelFolder : "Before", original.ModelFolder);
            Assert.AreEqual(commit, original.PropertySelectedFieldMap.ContainsKey(nameof(original.AiExecutionProvider)));
            Assert.AreSame(setup.ProviderField, project.FlowBloxRegistry.GetUserFields().Single());
            Assert.AreEqual(1, project.InputFiles.Count);
            StringAssert.Contains(project.InputFiles.Single().Command, setup.ProviderField.FullyQualifiedName);
        }
        finally
        {
            transaction.Cancel();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
            protocolOption.Value = previousProtocolDirectory;
        }
    }
}
