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
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Json
{
    [Display(Name = "JsonObjectFlowBlock_DisplayName", Description = "JsonObjectFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("JsonObjectFlowBlock_SpecialExplanation_ManagedResource", Icon = SpecialExplanationIcon.Information)]
    public class JsonObjectFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [JsonIgnore]
        [DeepCopierIgnore]
        public JObject InternalJsonObject { get; protected set; }

        [Display(Name = "JsonObjectFlowBlock_JsonContent", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true, SyntaxHighlighting = "JSON")]
        [Required]
        public string JsonContent { get; set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_json, 16, SKColors.DarkOrange);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_json, 32, SKColors.DarkOrange);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Json;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(JsonContent));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var content = this.Resolve(owner => owner.JsonContent);
                var token = JToken.Parse(content);
                if (token is JObject obj)
                    InternalJsonObject = obj;
                else
                    InternalJsonObject = new JObject { ["_"] = token };

                GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
            });
        }
    }
}
