using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FlowBlox.Core.Models.FlowBlocks.DateOperations
{
    [Display(Name = "DateTimeDifferenceFlowBlock_DisplayName", Description = "DateTimeDifferenceFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class DateTimeDifferenceFlowBlock : BaseSingleResultFlowBlock
    {
        [Required]
        [Display(Name = "DateTimeDifferenceFlowBlock_StartDate", Description = "DateTimeDifferenceFlowBlock_StartDate_Description", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleDateTimeFields), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
        public FieldElement StartDate { get; set; }

        [Required]
        [Display(Name = "DateTimeDifferenceFlowBlock_EndDate", Description = "DateTimeDifferenceFlowBlock_EndDate_Description", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleDateTimeFields), DisplayMember = nameof(FieldElement.FullyQualifiedName))]
        public FieldElement EndDate { get; set; }

        [Display(Name = "DateTimeDifferenceFlowBlock_Unit", Description = "DateTimeDifferenceFlowBlock_Unit_Description", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public DateTimeOperationUnit Unit { get; set; } = DateTimeOperationUnit.Days;

        [Display(Name = "DateTimeDifferenceFlowBlock_AbsoluteValue", Description = "DateTimeDifferenceFlowBlock_AbsoluteValue_Description", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public bool AbsoluteValue { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.timeline_clock, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.timeline_clock, 32, SKColors.SteelBlue);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.DateOperations;
        public override FieldTypes DefaultResultFieldType => FieldTypes.Double;

        public List<FieldElement> GetPossibleDateTimeFields() => DateTimeFlowBlockHelper.GetDateTimeFieldElements();

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(StartDate));
            properties.Add(nameof(EndDate));
            properties.Add(nameof(Unit));
            properties.Add(nameof(AbsoluteValue));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var startDate = DateTimeFlowBlockHelper.ResolveDateTime(StartDate, nameof(StartDate));
                var endDate = DateTimeFlowBlockHelper.ResolveDateTime(EndDate, nameof(EndDate));

                var diffValue = CalculateDifference(startDate, endDate, this.Resolve(x => x.Unit));
                if (this.Resolve(x => x.AbsoluteValue))
                    diffValue = Math.Abs(diffValue);

                GenerateResult(runtime, diffValue.ToString(CultureInfo.InvariantCulture));
            });
        }

        private static double CalculateDifference(DateTime startDate, DateTime endDate, DateTimeOperationUnit unit)
        {
            var timeSpan = endDate - startDate;
            return unit switch
            {
                DateTimeOperationUnit.Years => CalculateWholeYearsDifference(startDate, endDate),
                DateTimeOperationUnit.Months => CalculateWholeMonthsDifference(startDate, endDate),
                DateTimeOperationUnit.Days => timeSpan.TotalDays,
                DateTimeOperationUnit.Hours => timeSpan.TotalHours,
                DateTimeOperationUnit.Minutes => timeSpan.TotalMinutes,
                DateTimeOperationUnit.Seconds => timeSpan.TotalSeconds,
                _ => timeSpan.TotalDays
            };
        }

        private static int CalculateWholeYearsDifference(DateTime startDate, DateTime endDate)
        {
            var sign = 1;
            if (endDate < startDate)
            {
                (startDate, endDate) = (endDate, startDate);
                sign = -1;
            }

            var years = endDate.Year - startDate.Year;
            if (endDate < startDate.AddYears(years))
                years--;

            return years * sign;
        }

        private static int CalculateWholeMonthsDifference(DateTime startDate, DateTime endDate)
        {
            var sign = 1;
            if (endDate < startDate)
            {
                (startDate, endDate) = (endDate, startDate);
                sign = -1;
            }

            var months = (endDate.Year - startDate.Year) * 12 + (endDate.Month - startDate.Month);
            if (endDate.Day < startDate.Day)
                months--;

            return months * sign;
        }
    }
}
