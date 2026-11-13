using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.ControlFlow
{
    [Display(Name = "DelayFlowBlock_DisplayName", Description = "DelayFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class DelayFlowBlock : BaseFlowBlock
    {
        [Range(0, int.MaxValue)]
        [Display(Name = "DelayFlowBlock_DelayMilliseconds", Description = "DelayFlowBlock_DelayMilliseconds_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        public int DelayMilliseconds { get; set; } = 1000;
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calendar_clock, 16, SKColors.SteelBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calendar_clock, 32, SKColors.SteelBlue);
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.ControlFlow;
        public override bool Execute(Runtime.BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {
                if (this.Resolve(x => x.DelayMilliseconds) > 0) Thread.Sleep(this.Resolve(x => x.DelayMilliseconds));
                ExecuteNextFlowBlocks(runtime);
            });
        }
        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties(); properties.Add(nameof(DelayMilliseconds)); return properties;
        }
    }
}
