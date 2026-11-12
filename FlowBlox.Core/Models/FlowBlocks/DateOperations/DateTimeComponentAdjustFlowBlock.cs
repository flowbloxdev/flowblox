using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.DateOperations
{
    public enum DateTimeComponentAdjustmentMode
    {
        [Display(Name = "DateTimeComponentAdjustmentMode_Add", ResourceType = typeof(FlowBloxTexts))]
        Add,

        [Display(Name = "DateTimeComponentAdjustmentMode_Set", ResourceType = typeof(FlowBloxTexts))]
        Set
    }

    public enum DateTimeComponentAdjustmentTarget
    {
        [Display(Name = "DateTimeComponentAdjustmentTarget_Time", ResourceType = typeof(FlowBloxTexts))]
        Time,

        [Display(Name = "DateTimeComponentAdjustmentTarget_Year", ResourceType = typeof(FlowBloxTexts))]
        Year,

        [Display(Name = "DateTimeComponentAdjustmentTarget_Month", ResourceType = typeof(FlowBloxTexts))]
        Month,

        [Display(Name = "DateTimeComponentAdjustmentTarget_Day", ResourceType = typeof(FlowBloxTexts))]
        Day,

        [Display(Name = "DateTimeComponentAdjustmentTarget_Hours", ResourceType = typeof(FlowBloxTexts))]
        Hours,

        [Display(Name = "DateTimeComponentAdjustmentTarget_Minutes", ResourceType = typeof(FlowBloxTexts))]
        Minutes,

        [Display(Name = "DateTimeComponentAdjustmentTarget_Seconds", ResourceType = typeof(FlowBloxTexts))]
        Seconds
    }

    [Display(Name = "DateTimeComponentAdjustmentEntry_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    public class DateTimeComponentAdjustmentEntry : FlowBloxReactiveObject
    {
        [Display(Name = "DateTimeComponentAdjustmentEntry_Target", Description = "DateTimeComponentAdjustmentEntry_Target_Description", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public DateTimeComponentAdjustmentTarget Target { get; set; } = DateTimeComponentAdjustmentTarget.Day;

        [Display(Name = "DateTimeComponentAdjustmentEntry_Mode", Description = "DateTimeComponentAdjustmentEntry_Mode_Description", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public DateTimeComponentAdjustmentMode Mode { get; set; } = DateTimeComponentAdjustmentMode.Add;

        [Display(Name = "DateTimeComponentAdjustmentEntry_Value", Description = "DateTimeComponentAdjustmentEntry_Value_Description", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int Value { get; set; } = 1;

        public override string ToString() => $"{Mode} {Value} {Target}";
    }

    [FlowBloxUIGroup("DateTimeComponentAdjustFlowBlock_Groups_Adjustments", 0)]
    [Display(Name = "DateTimeComponentAdjustFlowBlock_DisplayName", Description = "DateTimeComponentAdjustFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class DateTimeComponentAdjustFlowBlock : BaseSingleResultFlowBlock
    {
        [Required]
        [Display(Name = "DateTimeComponentAdjustFlowBlock_SourceDate", Description = "DateTimeComponentAdjustFlowBlock_SourceDate_Description", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(
            Factory = UIFactory.Association,
            SelectionFilterMethod = nameof(GetPossibleDateTimeFields),
            SelectionDisplayMember = nameof(FieldElement.FullyQualifiedName),
            Operations = UIOperations.Link | UIOperations.Unlink)]
        public FieldElement SourceDate { get; set; }

        [Display(Name = "DateTimeComponentAdjustFlowBlock_Adjustments", Description = "DateTimeComponentAdjustFlowBlock_Adjustments_Description", ResourceType = typeof(FlowBloxTexts), GroupName = "DateTimeComponentAdjustFlowBlock_Groups_Adjustments", Order = 1)]
        [FlowBloxUI(Factory = UIFactory.GridView, DisplayLabel = false)]
        [FlowBloxDataGrid(GridColumnMemberNames =
        [
            nameof(DateTimeComponentAdjustmentEntry.Target),
            nameof(DateTimeComponentAdjustmentEntry.Mode),
            nameof(DateTimeComponentAdjustmentEntry.Value)
        ])]
        public ObservableCollection<DateTimeComponentAdjustmentEntry> Adjustments { get; set; } = new();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calendar_clock, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calendar_clock, 32, SKColors.SteelBlue);

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
            return properties;
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(DateTimeComponentAdjustNotifications));
                return notificationTypes;
            }
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var sourceDate = DateTimeFlowBlockHelper.ResolveDateTime(SourceDate, nameof(SourceDate));
                var result = sourceDate;

                if (Adjustments == null || Adjustments.Count == 0)
                {
                    CreateNotification(runtime, DateTimeComponentAdjustNotifications.NoAdjustmentsConfigured);
                }
                else
                {
                    foreach (var adjustment in Adjustments.ExceptNull())
                        result = ApplyAdjustment(result, adjustment);
                }

                var resultString = FieldResultFormatter.FormatResult(ResultField, result);
                GenerateResult(runtime, resultString);
            });
        }

        private static DateTime ApplyAdjustment(DateTime source, DateTimeComponentAdjustmentEntry adjustment)
        {
            var mode = adjustment.Resolve(entry => entry.Mode);
            var target = adjustment.Resolve(entry => entry.Target);
            var value = adjustment.Resolve(entry => entry.Value);
            return mode switch
            {
                DateTimeComponentAdjustmentMode.Add => ApplyAdd(source, target, value),
                DateTimeComponentAdjustmentMode.Set => ApplySet(source, target, value),
                _ => source
            };
        }

        private static DateTime ApplyAdd(DateTime source, DateTimeComponentAdjustmentTarget target, int value)
        {
            return target switch
            {
                DateTimeComponentAdjustmentTarget.Year => source.AddYears(value),
                DateTimeComponentAdjustmentTarget.Month => source.AddMonths(value),
                DateTimeComponentAdjustmentTarget.Day => source.AddDays(value),
                DateTimeComponentAdjustmentTarget.Hours => source.AddHours(value),
                DateTimeComponentAdjustmentTarget.Minutes => source.AddMinutes(value),
                DateTimeComponentAdjustmentTarget.Seconds => source.AddSeconds(value),
                DateTimeComponentAdjustmentTarget.Time => source.AddSeconds(value),
                _ => source
            };
        }

        private static DateTime ApplySet(DateTime source, DateTimeComponentAdjustmentTarget target, int value)
        {
            return target switch
            {
                DateTimeComponentAdjustmentTarget.Time => source.Date.AddSeconds(Math.Max(0, value)),
                DateTimeComponentAdjustmentTarget.Year => SetYear(source, value),
                DateTimeComponentAdjustmentTarget.Month => SetMonth(source, value),
                DateTimeComponentAdjustmentTarget.Day => SetDay(source, value),
                DateTimeComponentAdjustmentTarget.Hours => SetTimePart(source, value, source.Minute, source.Second),
                DateTimeComponentAdjustmentTarget.Minutes => SetTimePart(source, source.Hour, value, source.Second),
                DateTimeComponentAdjustmentTarget.Seconds => SetTimePart(source, source.Hour, source.Minute, value),
                _ => source
            };
        }

        private static DateTime SetYear(DateTime source, int value)
        {
            var year = Math.Clamp(value, 1, 9999);
            var day = Math.Min(source.Day, DateTime.DaysInMonth(year, source.Month));
            return new DateTime(year, source.Month, day, source.Hour, source.Minute, source.Second, source.Kind);
        }

        private static DateTime SetMonth(DateTime source, int value)
        {
            var month = Math.Clamp(value, 1, 12);
            var day = Math.Min(source.Day, DateTime.DaysInMonth(source.Year, month));
            return new DateTime(source.Year, month, day, source.Hour, source.Minute, source.Second, source.Kind);
        }

        private static DateTime SetDay(DateTime source, int value)
        {
            var maxDay = DateTime.DaysInMonth(source.Year, source.Month);
            var day = Math.Clamp(value, 1, maxDay);
            return new DateTime(source.Year, source.Month, day, source.Hour, source.Minute, source.Second, source.Kind);
        }

        private static DateTime SetTimePart(DateTime source, int hour, int minute, int second)
        {
            var h = Math.Clamp(hour, 0, 23);
            var m = Math.Clamp(minute, 0, 59);
            var s = Math.Clamp(second, 0, 59);
            return new DateTime(source.Year, source.Month, source.Day, h, m, s, source.Kind);
        }

        private void EnsureDateTimeResultFieldFormat()
        {
            if (ResultField?.FieldType == null)
                return;

            ResultField.FieldType.FieldType = FieldTypes.DateTime;
            if (string.IsNullOrWhiteSpace(ResultField.FieldType.DateFormat))
                ResultField.FieldType.DateFormat = "o";
        }

        public enum DateTimeComponentAdjustNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "DateTimeComponentAdjustFlowBlock_NoAdjustmentsConfigured", ResourceType = typeof(FlowBloxTexts))]
            NoAdjustmentsConfigured
        }
    }
}
