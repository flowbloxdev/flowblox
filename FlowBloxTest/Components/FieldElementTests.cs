using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Models.Components;
using FlowBlox.Test.Runtime;
using System.ComponentModel.DataAnnotations;

namespace FlowBloxTest.Components
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class FieldElementTests
    {
        [TestMethod]
        [DataRow(UserFieldTypes.Input, null)]
        [DataRow(UserFieldTypes.Input, " \r\n")]
        [DataRow(UserFieldTypes.Memory, "")]
        public void RuntimeStartup_RejectsMissingRequiredValuesBeforeExecuting(UserFieldTypes fieldType, string value)
        {
            var project = CreateProjectWithStartFlowBlock(out _);
            var field = project.FlowBloxRegistry.CreateUserField(fieldType, FieldTypes.Text, "RequiredValue");
            field.IsRequiredAtRuntimeStartup = true;
            field.StringValue = value;
            var runtime = new FlowBloxUnitTestRuntime(project);

            var exception = Assert.ThrowsExactly<ValidationException>(() => runtime.Execute());

            StringAssert.Contains(exception.Message, field.FullyQualifiedName);
        }

        [TestMethod]
        public void RuntimeStartup_RejectsRequiredMemoryThatWillBeCleared()
        {
            var project = CreateProjectWithStartFlowBlock(out _);
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Memory");
            field.StringValue = "Existing value";
            field.IsRequiredAtRuntimeStartup = true;
            field.ClearOnRuntimeStart = true;

            Assert.ThrowsExactly<ValidationException>(() => new FlowBloxUnitTestRuntime(project).Execute());
            Assert.AreEqual("Existing value", field.StringValue);
        }

        [TestMethod]
        public void StartupValueList_RestrictsOnlyPossibleValuesWithoutDisclosingValues()
        {
            var project = CreateProjectWithStartFlowBlock(out _);
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "Choice");
            field.ListOfValues.Add(new ValueItem { Value = "Allowed" });
            field.StringValue = "Other confidential value";
            var runtime = new FlowBloxUnitTestRuntime(project);
            runtime.ValidateUserFieldsAtRuntimeStartup();

            field.LoVType = LoVType.PossibleValues;
            var exception = Assert.ThrowsExactly<ValidationException>(runtime.ValidateUserFieldsAtRuntimeStartup);
            Assert.IsFalse(exception.Message.Contains(field.StringValue, StringComparison.Ordinal));
            field.StringValue = "Allowed";
            runtime.ValidateUserFieldsAtRuntimeStartup();
            field.StringValue = "";
            runtime.ValidateUserFieldsAtRuntimeStartup();
        }

        [TestMethod]
        public void TestExecution_ValidatesRequiredInputsAfterApplyingTestValues()
        {
            var project = CreateProjectWithStartFlowBlock(out var start);
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, FieldTypes.Text, "Input");
            field.IsRequiredAtRuntimeStartup = true;
            var definition = new FlowBloxTestDefinition { Name = "Required input" };
            start.TestDefinitions.Add(definition);
            var input = definition.Entries.Single(x => x.FlowBlock == null)
                .FlowBloxTestConfigurations.Single(x => x.FieldElement == field);
            input.SelectionMode = FlowBloxTestConfigurationSelectionMode.UserInput;
            input.UserInput = "Test value";
            var executor = new FlowBloxTestExecutor();
            executor.Initialize(definition, start);
            try
            {
                Assert.IsTrue(executor.ExecuteTest().Success);
                input.UserInput = "";
                Assert.ThrowsExactly<ValidationException>(() => executor.ExecuteTest());
            }
            finally
            {
                executor.Shutdown();
            }
        }

        [TestMethod]
        [DataRow(UserFieldTypes.Memory, true, "")]
        [DataRow(UserFieldTypes.Memory, false, "Previous run")]
        [DataRow(UserFieldTypes.Input, true, "Previous run")]
        public void RuntimeStarted_ClearsOnlyOptedInMemoryFields(UserFieldTypes fieldType, bool clear, string expected)
        {
            var project = CreateProjectWithStartFlowBlock(out _);
            var field = project.FlowBloxRegistry.CreateUserField(fieldType, FieldTypes.Text, "Value");
            field.StringValue = "Previous run";
            field.ClearOnRuntimeStart = clear;
            var runtime = new TransientRuntime(project);

            field.RuntimeStarted(runtime);

            Assert.AreEqual(expected, field.StringValue);

            field.SetValueWithoutEvaluation(runtime, "Next run");
            field.RuntimeStarted(runtime);

            Assert.AreEqual(clear && fieldType == UserFieldTypes.Memory ? "" : "Next run", field.StringValue);
        }

        [TestMethod]
        public void RenameMemoryField_UpdatesFullyQualifiedNameInFieldSelectionProperty()
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var field = project.FlowBloxRegistry.CreateUserField(
                UserFieldTypes.Memory,
                FieldTypes.Text,
                "PreviousName");
            var formatFlowBlock = new FormatFlowBlock
            {
                Name = "Format",
                FormatExpression = $"Value: {field.FullyQualifiedName}"
            };
            formatFlowBlock.OnAfterLoad();

            field.Name = "CurrentName";

            Assert.AreEqual("Value: $User::CurrentName", formatFlowBlock.FormatExpression);
        }

        [TestMethod]
        public void ReleaseAllRuntimeValues_RestoresPersistentUserFieldValue()
        {
            var project = CreateProjectWithStartFlowBlock(out _);
            var field = project.FlowBloxRegistry.CreateUserField(
                UserFieldTypes.Input,
                FieldTypes.Text,
                "Input");
            field.StringValue = "Original";

            var firstRuntime = new TransientRuntime(project);
            field.SetValueWithoutEvaluation(firstRuntime, "First test value", runtimeBound: true);
            Assert.AreEqual("First test value", field.StringValue);

            field.ReleaseAllRuntimeValues();

            Assert.AreEqual("Original", field.StringValue);
        }

        [TestMethod]
        public void RuntimeBoundValue_IsBoundWhenItEqualsCurrentlyVisibleValue()
        {
            var project = CreateProjectWithStartFlowBlock(out _);
            var field = project.FlowBloxRegistry.CreateUserField(
                UserFieldTypes.Input,
                FieldTypes.Text,
                "Input");
            field.StringValue = "Same value";
            var runtime = new TransientRuntime(project);

            field.SetValueWithoutEvaluation(runtime, "Same value", runtimeBound: true);
            field.StringValue = "Changed persistent value";

            Assert.AreEqual("Same value", field.StringValue);

            field.ReleaseRuntimeValue(runtime);
            Assert.AreEqual("Changed persistent value", field.StringValue);
        }

        [TestMethod]
        public void ExecuteTest_WithKeep_ReleasesPreviousUserFieldRuntimeValuesAtTestStart()
        {
            var project = CreateProjectWithStartFlowBlock(out var startFlowBlock);
            var field = project.FlowBloxRegistry.CreateUserField(
                UserFieldTypes.Input,
                FieldTypes.Text,
                "Input");
            field.StringValue = "Original";

            var previousRuntime = new TransientRuntime(project);
            field.SetValueWithoutEvaluation(previousRuntime, "Previous test value", runtimeBound: true);

            var testDefinition = new FlowBloxTestDefinition { Name = "Keep user field" };
            startFlowBlock.TestDefinitions.Add(testDefinition);
            var userFieldConfiguration = testDefinition.Entries
                .Single(x => x.FlowBlock == null)
                .FlowBloxTestConfigurations
                .Single(x => x.FieldElement == field);
            userFieldConfiguration.SelectionMode = FlowBloxTestConfigurationSelectionMode.Keep;

            var executor = new FlowBloxTestExecutor();
            executor.Initialize(testDefinition, startFlowBlock);
            var result = executor.ExecuteTest();
            executor.Shutdown();

            Assert.IsTrue(result.Success);
            Assert.AreEqual("Original", result.FieldValueAssignments[field.FullyQualifiedName]);
            Assert.AreEqual("Original", field.StringValue);
        }

        private static FlowBloxProject CreateProjectWithStartFlowBlock(out StartFlowBlock startFlowBlock)
        {
            var project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = project;

            startFlowBlock = project.FlowBloxRegistry.CreateFlowBlockUnregistered<StartFlowBlock>();
            project.FlowBloxRegistry.PostProcessFlowBlockCreated(startFlowBlock);
            project.FlowBloxRegistry.Register(startFlowBlock);
            return project;
        }
    }
}
