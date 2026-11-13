using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "TextBuilderFlowBlock_DisplayName", Description = "TextBuilderFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("TextBuilderFlowBlock_SpecialExplanation_ManagedResource", Icon = SpecialExplanationIcon.Information)]
    public class TextBuilderFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [JsonIgnore]
        [DeepCopierIgnore]
        public StringBuilder InternalStringBuilder { get; protected set; }

        [Display(Name = "TextBuilderFlowBlock_InitialText", Description = "TextBuilderFlowBlock_InitialText_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxTextBox(MultiLine = true)]
        public string InitialText { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_box_edit_outline, 16, SKColors.SlateBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_box_edit_outline, 32, SKColors.SlateBlue);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(InitialText));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var initialText = this.Resolve(owner => owner.InitialText);
                InternalStringBuilder = new StringBuilder(initialText);
                GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
            });
        }
    }
}
