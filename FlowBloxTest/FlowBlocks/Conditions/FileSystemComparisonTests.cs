using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.FlowBlocks.Conditions;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FileSystemComparisonTests : FlowBloxTestsBase
{
    [TestMethod]
    public void FileExists_ControlsBlockActivationUsingTheCurrentFieldPath()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var file = Path.GetTempFileName();
        try
        {
            var path = CreateUserField("DocumentPath");
            path.StringValue = file;
            var block = CreateFlowBlock<StartFlowBlock>();
            block.ActivationConditions.Add(new FieldLogicalComparisonCondition
            {
                FieldElement = path,
                Operator = ComparisonOperator.FileExists
            });

            Assert.IsTrue(block.ValidateRequirements(out _));
            path.StringValue = Path.GetDirectoryName(file);
            Assert.IsFalse(block.ValidateRequirements(out _));
            path.StringValue = file;
            File.Delete(file);
            Assert.IsFalse(block.ValidateRequirements(out _));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void DirectoryExists_ReevaluatesDecisionAndLogicalConditionsWithoutAComparisonValue()
    {
        var directory = Directory.CreateTempSubdirectory("flowblox-path-condition-");
        try
        {
            var path = new FieldElement(FieldTypes.Text) { StringValue = directory.FullName };
            var decision = new FieldComparisonCondition
            {
                FieldElement = path,
                Operator = ComparisonOperator.DirectoryExists
            };
            var logical = new FieldLogicalComparisonCondition
            {
                FieldElement = path,
                Operator = ComparisonOperator.DirectoryExists
            };

            Assert.IsTrue(decision.Compare());
            Assert.IsTrue(logical.Check());
            directory.Delete();
            Assert.IsFalse(decision.Compare());
            Assert.IsFalse(logical.Check());
            path.StringValue = null;
            Assert.IsFalse(decision.Compare());
        }
        finally
        {
            if (Directory.Exists(directory.FullName))
                directory.Delete();
        }
    }

    [TestMethod]
    public void EmptyFileCondition_ReevaluatesCurrentFieldPathAfterContentChanges()
    {
        var project = new FlowBloxProject();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var file = Path.GetTempFileName();
        try
        {
            var path = CreateUserField("FilePath");
            path.StringValue = file;
            var condition = new FieldLogicalComparisonCondition
            {
                FieldElement = path,
                Operator = ComparisonOperator.FileIsEmpty
            };

            Assert.IsTrue(condition.Check());
            File.WriteAllText(file, " ");
            Assert.IsFalse(condition.Check());
            File.Delete(file);
            Assert.IsTrue(condition.Check());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void EmptyDirectoryCondition_SearchesRecursivelyForFilesAndIgnoresEmptySubdirectories()
    {
        var directory = Directory.CreateTempSubdirectory("flowblox-empty-condition-");
        var child = Path.Combine(directory.FullName, "child");
        var condition = new ComparisonCondition { Operator = ComparisonOperator.DirectoryIsEmpty };
        try
        {
            Assert.IsTrue(condition.Compare(directory.FullName));
            Directory.CreateDirectory(child);
            Assert.IsTrue(condition.Compare(directory.FullName));
            var file = Path.Combine(child, "nested.txt");
            File.WriteAllText(file, string.Empty);
            Assert.IsFalse(condition.Compare(directory.FullName));
            File.Delete(file);
            Assert.IsTrue(condition.Compare(directory.FullName));
            Directory.Delete(child);
            Directory.Delete(directory.FullName);
            Assert.IsTrue(condition.Compare(directory.FullName));
        }
        finally
        {
            var file = Path.Combine(child, "nested.txt");
            if (File.Exists(file))
                File.Delete(file);
            if (Directory.Exists(child))
                Directory.Delete(child);
            if (Directory.Exists(directory.FullName))
                Directory.Delete(directory.FullName);
        }
    }

}
