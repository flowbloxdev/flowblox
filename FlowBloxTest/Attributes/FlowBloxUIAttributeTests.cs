using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Placeholders.GenerationStrategy;

namespace FlowBloxTest.Attributes;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FlowBloxUIAttributeTests
{
    private static ValidationResult? Validate(object? value, bool fieldSelectionEnabled = true)
    {
        var attribute = new FlowBloxUIAttribute
        {
            UiOptions = fieldSelectionEnabled ? UIOptions.EnableFieldSelection : 0
        };
        return attribute.GetValidationResult(value, new ValidationContext(new object()) { MemberName = "Prompt" });
    }

    [TestMethod]
    [DataRow("$GenerationStrategy::InputFieldValue")]
    [DataRow("$GenerationStrategy::TestExpectations")]
    [DataRow("$GenerationStrategy::TargetPropertyDescription")]
    [DataRow("$generationstrategy::TestExpectations")]
    [DataRow("$Project::Directory")]
    [DataRow("$Options::Paths.InputDir")]
    [DataRow("$User::NotYetCreated")]
    [DataRow("$InputFile::Path")]
    [DataRow("$TestDefinition::Name")]
    public void BuiltInSource_DoesNotRequireRegistryField(string placeholder)
    {
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxProject().FlowBloxRegistry);
        Assert.AreEqual(ValidationResult.Success, Validate(placeholder));
    }

    [TestMethod]
    public void PublishedGenerationStrategyPlaceholders_AreAccepted()
    {
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxProject().FlowBloxRegistry);
        var prompt = string.Join(Environment.NewLine,
            FlowBloxGenerationStrategyPlaceholderProvider.GetElements().Select(x => x.Placeholder));
        Assert.AreEqual(ValidationResult.Success, Validate(prompt));
    }

    [TestMethod]
    [DataRow("$MissingBlock::Value")]
    [DataRow("$GenerationStrategyy::TestExpectations")]
    [DataRow("$GenerationStrategy::TestExpectations $MissingBlock::Value")]
    public void MissingFlowBlockField_IsStillRejected(string text)
    {
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxProject().FlowBloxRegistry);
        var result = Validate(text);
        Assert.IsNotNull(result);
        CollectionAssert.AreEqual(new[] { "Prompt" }, result.MemberNames.ToArray());
    }

    [TestMethod]
    public void ExistingFlowBlockField_IsAcceptedAlongsideBuiltInSources()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var block = new FormatFlowBlock { Name = "Format" };
        var field = new FieldElement(block) { Name = "Value" };
        block.Fields.Add(field);
        block.OnAfterLoad();
        project.FlowBloxRegistry.Register(field);
        Assert.IsTrue(project.FlowBloxRegistry.HasField(field.FullyQualifiedName));
        Assert.AreEqual(ValidationResult.Success, Validate($"$GenerationStrategy::TestExpectations {field.FullyQualifiedName}"));
    }

    [TestMethod]
    public void WithoutFieldSelection_FieldReferencesAreNotValidated()
    {
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxProject().FlowBloxRegistry);
        Assert.AreEqual(ValidationResult.Success, Validate("$MissingBlock::Value", fieldSelectionEnabled: false));
        Assert.AreEqual(ValidationResult.Success, Validate(null));
        Assert.AreEqual(ValidationResult.Success, Validate(42));
    }
}
