using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.DateOperations
{
    [Display(Name = "DateTimeArithmeticFlowBlock_DisplayName", Description = "DateTimeArithmeticFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class DateTimeArithmeticFlowBlock : BaseSingleResultFlowBlock
    {
        [Required]
        [Display(Name = "DateTimeArithmeticFlowBlock_SourceDate", Description = "DateTimeArithmeticFlowBlock_SourceDate_Description", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(
            Factory = UIFactory.Association,
            SelectionFilterMethod = nameof(GetPossibleDateTimeFields),
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            Operations = UIOperations.Link | UIOperations.Unlink)]
        public FieldElement SourceDate { get; set; }

        [Display(Name = "DateTimeArithmeticFlowBlock_Unit", Description = "DateTimeArithmeticFlowBlock_Unit_Description", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public DateTimeOperationUnit Unit { get; set; } = DateTimeOperationUnit.Days;

        [Display(Name = "DateTimeArithmeticFlowBlock_Offset", Description = "DateTimeArithmeticFlowBlock_Offset_Description", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int Offset { get; set; } = 1;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calendar_plus, 16, SKColors.DarkOrange);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calendar_plus, 32, SKColors.DarkOrange);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.DateOperations;
        public override FieldTypes DefaultResultFieldType => FieldTypes.DateTime;

        public override void OnAfterCreate()
        {
            base.OnAfterCreate();
            EnsureDateTimeResultFieldFormat();
        }

        public List<FieldElement> GetPossibleDateTimeFields() => DateTimeFlowBlockHelper.GetDateTimeFieldElements();

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(SourceDate));
            properties.Add(nameof(Unit));
            properties.Add(nameof(Offset));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var sourceDate = DateTimeFlowBlockHelper.ResolveDateTime(SourceDate, nameof(SourceDate));
                var resultDate = ApplyOperation(sourceDate, Unit, Offset);
                var resultString = FieldResultFormatter.FormatResult(ResultField, resultDate);
                GenerateResult(runtime, resultString);
            });
        }

        private static DateTime ApplyOperation(DateTime source, DateTimeOperationUnit unit, int offset)
        {
            return unit switch
            {
                DateTimeOperationUnit.Years => source.AddYears(offset),
                DateTimeOperationUnit.Months => source.AddMonths(offset),
                DateTimeOperationUnit.Days => source.AddDays(offset),
                DateTimeOperationUnit.Hours => source.AddHours(offset),
                DateTimeOperationUnit.Minutes => source.AddMinutes(offset),
                DateTimeOperationUnit.Seconds => source.AddSeconds(offset),
                _ => source
            };
        }

        private void EnsureDateTimeResultFieldFormat()
        {
            if (ResultField?.FieldType == null)
                return;

            ResultField.FieldType.FieldType = FieldTypes.DateTime;
            if (string.IsNullOrWhiteSpace(ResultField.FieldType.DateFormat))
                ResultField.FieldType.DateFormat = "o";
        }
    }
}
