using System.Text;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.LocalAI;

namespace FlowBloxTest.LocalAI;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class LocalAISetupTests
{
    [TestMethod]
    public void RepeatedSetup_ReusesProviderAndScriptAcrossBlocks()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var first = AddBlock(project, new OnnxGenAIFlowBlock { Name = "First" });
        var second = AddBlock(project, new OnnxGenAIFlowBlock { Name = "Second" });
        var provider = new FieldElement(FieldTypes.Text)
        {
            Name = "ComputeProvider",
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            DefaultValue = "Default"
        };
        project.FlowBloxRegistry.RegisterManagedObject(provider);
        first.PropertySelectedFieldMap[nameof(first.AiExecutionProvider)] = provider.FullyQualifiedName;
        var initial = Configure(project, first, LocalAIModel.Qwen3Instruct);
        Configure(project, first, LocalAIModel.Qwen3Instruct);
        var repeated = Configure(project, second, LocalAIModel.Qwen3Instruct);

        Assert.HasCount(1, project.FlowBloxRegistry.GetUserFields().ToList());
        Assert.HasCount(1, project.InputFiles);
        Assert.AreSame(initial.ProviderField, repeated.ProviderField);
        Assert.AreEqual(
            first.PropertySelectedFieldMap[nameof(first.AiExecutionProvider)],
            second.PropertySelectedFieldMap[nameof(second.AiExecutionProvider)]);
        Assert.AreEqual(3, repeated.ProviderField.ListOfValues.Count);
        Assert.AreEqual(LoVType.PossibleValues, repeated.ProviderField.LoVType);
        Assert.IsTrue(repeated.ConfigureInputFile());
    }

    [TestMethod]
    public void DifferentProviderForSharedScript_DoesNotBreakExistingSetup()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var first = AddBlock(project, new OnnxGenAIFlowBlock { Name = "First" });
        Configure(project, first, LocalAIModel.Qwen3Instruct);
        var originalCommand = project.InputFiles.Single().Command;
        var otherProvider = new FieldElement(FieldTypes.Text)
        {
            Name = "OtherProvider",
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            DefaultValue = "CUDA"
        };
        project.FlowBloxRegistry.RegisterManagedObject(otherProvider);
        var second = AddBlock(project, new OnnxGenAIFlowBlock { Name = "Second" });
        second.PropertySelectedFieldMap[nameof(second.AiExecutionProvider)] = otherProvider.FullyQualifiedName;
        var conflicting = new LocalAISetup(project, second, LocalAIModel.Qwen3Instruct, _ => "script");

        Assert.ThrowsExactly<InvalidOperationException>(() => conflicting.Prepare());

        Assert.AreEqual(originalCommand, project.InputFiles.Single().Command);
        Assert.AreEqual(otherProvider.FullyQualifiedName, second.PropertySelectedFieldMap[nameof(second.AiExecutionProvider)]);
    }

    [TestMethod]
    public void Setup_ReusesMappedInputAndPreservesUserPrompts()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = AddBlock(project, new OnnxGenAIFlowBlock
        {
            Name = "Answer",
            Prompt = "$User::Question",
            SystemPrompt = "Use only supplied context."
        });
        var provider = new FieldElement(FieldTypes.Text)
        {
            Name = "ComputeProvider",
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            DefaultValue = "CUDA"
        };
        project.FlowBloxRegistry.RegisterManagedObject(provider);
        block.PropertySelectedFieldMap[nameof(block.AiExecutionProvider)] = provider.FullyQualifiedName;

        var setup = Configure(project, block, LocalAIModel.Phi4Mini);

        Assert.AreSame(provider, setup.ProviderField);
        Assert.AreEqual("CUDA", provider.DefaultValue);
        StringAssert.Contains(project.InputFiles.Single().Command, $"--provider \"{provider.FullyQualifiedName}\"");
        StringAssert.Contains(project.InputFiles.Single().Command, $"--model-directory-link \"{block.ModelFolder}\"");
        Assert.AreEqual("$User::Question", block.Prompt);
        Assert.AreEqual("Use only supplied context.", block.SystemPrompt);
        Assert.IsTrue(provider.IsRequiredAtRuntimeStartup);
    }

    [TestMethod]
    public void IncompatibleExistingProvider_FailsBeforeChangingProject()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = AddBlock(project, new OnnxGenAIFlowBlock { ModelFolder = "existing-model" });
        var memory = new FieldElement(FieldTypes.Text)
        {
            Name = "OnnxProvider",
            UserField = true,
            UserFieldType = UserFieldTypes.Memory,
            DefaultValue = "unrelated value"
        };
        project.FlowBloxRegistry.RegisterManagedObject(memory);
        var setup = new LocalAISetup(project, block, LocalAIModel.Qwen3Instruct, _ => "script");

        Assert.ThrowsExactly<InvalidOperationException>(() => setup.Prepare());

        Assert.IsEmpty(project.InputFiles);
        Assert.AreEqual("existing-model", block.ModelFolder);
        Assert.AreEqual("unrelated value", memory.DefaultValue);
        Assert.IsFalse(memory.IsRequiredAtRuntimeStartup);
    }

    [TestMethod]
    public void MissingScript_PreventsPartialSetup()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = AddBlock(project, new OnnxQAFlowBlock());
        var setup = new LocalAISetup(project, block, LocalAIModel.ExtractiveQA, _ => " ");

        Assert.ThrowsExactly<InvalidOperationException>(() => setup.Prepare());
        Assert.ThrowsExactly<InvalidOperationException>(() => setup.ConfigureProvider());
        Assert.IsEmpty(project.FlowBloxRegistry.GetUserFields().ToList());
        Assert.IsEmpty(project.InputFiles);
    }

    [TestMethod]
    public void ProviderChanges_AreResolvedByBothOnnxBlockTypes()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var genAI = AddBlock(project, new OnnxGenAIFlowBlock { Name = "Generate" });
        var qa = AddBlock(project, new OnnxQAFlowBlock { Name = "Extract" });
        var setup = Configure(project, genAI, LocalAIModel.Qwen3Instruct);
        Configure(project, qa, LocalAIModel.ExtractiveQA);

        Assert.AreEqual(AiExecutionProviders.Default, genAI.Resolve(block => block.AiExecutionProvider));
        Assert.AreEqual(AiExecutionProviders.Default, qa.Resolve(block => block.AiExecutionProvider));
        setup.ProviderField.StringValue = "DirectML";
        Assert.AreEqual(AiExecutionProviders.DirectML, genAI.Resolve(block => block.AiExecutionProvider));
        Assert.AreEqual(AiExecutionProviders.DirectML, qa.Resolve(block => block.AiExecutionProvider));
    }

    [TestMethod]
    public void QASetup_RejectsGenerativePresetAndBindsPortableExport()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = AddBlock(project, new OnnxQAFlowBlock { Name = "Extract" });
        var incompatible = new LocalAISetup(project, block, LocalAIModel.Phi4Mini, _ => "script");
        Assert.ThrowsExactly<InvalidOperationException>(() => incompatible.Prepare());

        var setup = Configure(project, block, LocalAIModel.ExtractiveQA);

        var file = project.InputFiles.Single();
        StringAssert.Contains(file.Command, $"--provider \"{setup.ProviderField.FullyQualifiedName}\"");
        Assert.AreEqual(setup.ModelFolder, block.ModelFolder);
        Assert.AreEqual(setup.ProviderField.FullyQualifiedName, block.PropertySelectedFieldMap[nameof(block.AiExecutionProvider)]);
        Assert.IsTrue(file.ExecuteBeforeRuntime);
        Assert.AreEqual(FlowBloxInputFileSyncMode.AlwaysOverwrite, file.SyncMode);
        Assert.AreEqual("bundled script", Encoding.UTF8.GetString(file.ContentBytes));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConflictingScript_IsChangedOnlyWithExplicitOverwrite(bool overwrite)
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = AddBlock(project, new OnnxGenAIFlowBlock { ModelFolder = "Before" });
        var file = new FlowBloxInputFile
        {
            RelativePath = "download_qwen3_instruct_onnx.py",
            ContentBytes = Encoding.UTF8.GetBytes("custom script"),
            Command = "custom command"
        };
        project.InputFiles.Add(file);
        var setup = new LocalAISetup(project, block, LocalAIModel.Qwen3Instruct, _ => "bundled script", new LocalAISetupOptions { OverwriteProviderInput = overwrite, OverwriteScript = overwrite });
        if (!overwrite)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => setup.Prepare());
            Assert.IsEmpty(project.FlowBloxRegistry.GetUserFields().ToList());
            Assert.AreEqual("custom command", file.Command);
            Assert.AreEqual("custom script", Encoding.UTF8.GetString(file.ContentBytes));
            Assert.AreEqual("Before", block.ModelFolder);
            return;
        }

        setup.Prepare();
        setup.ConfigureProvider();
        setup.ConfigureInputFile();
        setup.ConfigureBlock();
        Assert.AreSame(file, project.InputFiles.Single());
        Assert.AreEqual("bundled script", Encoding.UTF8.GetString(file.ContentBytes));
        Assert.IsTrue(file.ExecuteBeforeRuntime);
        Assert.AreEqual(FlowBloxInputFileSyncMode.AlwaysOverwrite, file.SyncMode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConflictingProvider_IsChangedOnlyWithExplicitOverwrite(bool overwrite)
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = AddBlock(project, new OnnxGenAIFlowBlock());
        var provider = new FieldElement(FieldTypes.Boolean)
        {
            Name = "OnnxProvider",
            UserField = true,
            UserFieldType = UserFieldTypes.Memory,
            IsPassword = true,
            DefaultValue = "unrelated"
        };
        project.FlowBloxRegistry.RegisterManagedObject(provider);
        var setup = new LocalAISetup(project, block, LocalAIModel.Qwen3Instruct, _ => "bundled script", new LocalAISetupOptions { OverwriteProviderInput = overwrite, OverwriteScript = overwrite });
        if (!overwrite)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => setup.Prepare());
            Assert.AreEqual(UserFieldTypes.Memory, provider.UserFieldType);
            Assert.IsTrue(provider.IsPassword);
            Assert.IsEmpty(project.InputFiles);
            return;
        }

        setup.Prepare();
        setup.ConfigureProvider();
        setup.ConfigureInputFile();
        setup.ConfigureBlock();
        Assert.AreSame(provider, setup.ProviderField);
        Assert.AreEqual(UserFieldTypes.Input, provider.UserFieldType);
        Assert.AreEqual(FieldTypes.Text, provider.FieldType.FieldType);
        Assert.IsFalse(provider.IsPassword);
        Assert.AreEqual(LoVType.PossibleValues, provider.LoVType);
        foreach (var value in provider.ListOfValues)
        {
            provider.StringValue = value.Value;
            Assert.AreEqual(Enum.Parse<AiExecutionProviders>(value.Value), block.Resolve(item => item.AiExecutionProvider));
        }
    }

    [TestMethod]
    [DataRow("model")]
    [DataRow("provider")]
    [DataRow("mapping")]
    public void ExistingBlockSelections_RequireTheirOwnOverwriteOptionBeforeGlobalChanges(string existing)
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = AddBlock(project, new OnnxGenAIFlowBlock
        {
            ModelFolder = existing == "model" ? "selected-model" : string.Empty,
            AiExecutionProvider = existing == "provider" ? AiExecutionProviders.CUDA : null
        });
        if (existing == "mapping")
            block.PropertySelectedFieldMap[nameof(block.AiExecutionProvider)] = "$User::SelectedProvider";
        var setup = new LocalAISetup(project, block, LocalAIModel.Qwen3Instruct, _ => "bundled script", new LocalAISetupOptions
        {
            OverwriteBlockModel = false,
            OverwriteBlockProvider = false
        });
        Assert.ThrowsExactly<InvalidOperationException>(() => setup.Prepare());
        Assert.IsEmpty(project.FlowBloxRegistry.GetUserFields().ToList());
        Assert.IsEmpty(project.InputFiles);

        var replacing = Configure(project, block, LocalAIModel.Qwen3Instruct);
        Assert.AreEqual(replacing.ModelFolder, block.ModelFolder);
        Assert.AreEqual(replacing.ProviderField.FullyQualifiedName, block.PropertySelectedFieldMap[nameof(block.AiExecutionProvider)]);
        Assert.AreEqual(AiExecutionProviders.Default, block.Resolve(item => item.AiExecutionProvider));
    }

    [TestMethod]
    public void EmptyBlockSelections_CanBeConfiguredWithoutOverwrite()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = AddBlock(project, new OnnxGenAIFlowBlock { ModelFolder = string.Empty, AiExecutionProvider = null });
        var setup = new LocalAISetup(project, block, LocalAIModel.Qwen3Instruct, _ => "bundled script", new LocalAISetupOptions
        {
            OverwriteBlockModel = false,
            OverwriteBlockProvider = false
        });
        setup.Prepare();
        setup.ConfigureProvider();
        setup.ConfigureInputFile();
        setup.ConfigureBlock();
        Assert.AreEqual(setup.ModelFolder, block.ModelFolder);
        Assert.AreEqual(AiExecutionProviders.Default, block.Resolve(item => item.AiExecutionProvider));
    }

    private static T AddBlock<T>(FlowBloxProject project, T block) where T : BaseFlowBlock
    {
        project.FlowBloxRegistry.RegisterFlowBlock(block);
        return block;
    }

    private static LocalAISetup Configure(FlowBloxProject project, BaseFlowBlock block, LocalAIModel model)
    {
        var setup = new LocalAISetup(project, block, model, _ => "bundled script");
        setup.Prepare();
        setup.ConfigureProvider();
        setup.ConfigureInputFile();
        setup.ConfigureBlock();
        return setup;
    }
}
