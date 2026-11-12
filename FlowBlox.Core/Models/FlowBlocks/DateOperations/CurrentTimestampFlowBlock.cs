using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.DateOperations
{
    [Display(Name = "CurrentTimestampFlowBlock_DisplayName", Description = "CurrentTimestampFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class CurrentTimestampFlowBlock : BaseSingleResultFlowBlock
    {
        [Display(Name = "CurrentTimestampFlowBlock_UseUtc", Description = "CurrentTimestampFlowBlock_UseUtc_Description", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool UseUtc { get; set; } = true;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calendar_clock, 16, SKColors.DarkSlateBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calendar_clock, 32, SKColors.DarkSlateBlue);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.DateOperations;
        public override FieldTypes DefaultResultFieldType => FieldTypes.DateTime;

        public override void OnAfterCreate()
        {
            base.OnAfterCreate();
            EnsureDateTimeResultFieldFormat();
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(UseUtc));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var timestamp = UseUtc ? DateTime.UtcNow : DateTime.Now;
                var resultString = FieldResultFormatter.FormatResult(ResultField, timestamp);
                GenerateResult(runtime, resultString);
            });
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
