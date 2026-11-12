using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Format;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;

namespace FlowBloxTest.FlowBlocks.TextOperations
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class FormatFlowBlockTests : FlowBloxTestsBase
    {
        private FlowBloxProject _project;

        [TestInitialize]
        public void Initialize()
        {
            _project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = _project;
        }

        [TestMethod]
        public void Validation_RequiresFormatExpressionByDefault()
        {
            var format = CreateFlowBlock<FormatFlowBlock>();

            var errors = format.ValidateObject();

            Assert.IsTrue(errors.ContainsKey(nameof(FormatFlowBlock.FormatExpression)));
        }

        [TestMethod]
        public void Validation_AllowsEmptyFormatExpressionWhenEnabled()
        {
            var format = CreateFlowBlock<FormatFlowBlock>();
            format.AllowEmptyFormatExpression = true;

            var errors = format.ValidateObject();

            Assert.IsFalse(errors.ContainsKey(nameof(FormatFlowBlock.FormatExpression)));
        }

        [TestMethod]
        public void Execute_AllowedEmptyFormatExpression_GeneratesEmptyString()
        {
            var start = CreateFlowBlock<StartFlowBlock>();
            var format = CreateFlowBlock<FormatFlowBlock>(start);
            format.AllowEmptyFormatExpression = true;

            CreateRuntimeAndExecute(_project);

            Assert.AreEqual(string.Empty, format.ResultField.StringValue);
            Assert.AreEqual(string.Empty, format.GridElementResult.Results.Single()
                .FieldValueMappings.Single().Value);
        }

        [TestMethod]
        public void Execute_UsesTypedDateTimeBooleanAndTextParameters()
        {
            var start = CreateFlowBlock<StartFlowBlock>();
            var format = CreateFlowBlock<FormatFlowBlock>(start);
            format.FormatExpression = "{0:yyyy-MM-dd}|{1}|{2}";

            var date = CreateUserField("Date");
            date.FieldType.FieldType = FieldTypes.DateTime;
            date.FieldType.DateFormat = "yyyy-MM-dd HH:mm:ss";
            date.StringValue = "2026-10-08 14:30:00";

            var enabled = CreateUserField("Enabled");
            enabled.FieldType.FieldType = FieldTypes.Boolean;
            enabled.StringValue = "true";

            var text = CreateUserField("Text");
            text.StringValue = "ready";

            format.FormatParameterDefinitions.Add(new FormatParameterDefinition { Field = date });
            format.FormatParameterDefinitions.Add(new FormatParameterDefinition { Field = enabled });
            format.FormatParameterDefinitions.Add(new FormatParameterDefinition { Field = text });

            CreateRuntimeAndExecute(_project);

            Assert.AreEqual("2026-10-08|True|ready", format.ResultField.StringValue);
        }
    }
}
