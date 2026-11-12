using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Json
{
    [Display(Name = "JsonObjectOutputFlowBlock_DisplayName", Description = "JsonObjectOutputFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("JsonObjectOutputFlowBlock_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    public class JsonObjectOutputFlowBlock : BaseSingleResultFlowBlock
    {
        [Display(Name = "JsonObjectOutputFlowBlock_AssociatedJsonObject", Description = "JsonObjectOutputFlowBlock_AssociatedJsonObject_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [AssociatedFlowBlockResolvable()]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Link | UIOperations.Unlink,
            SelectionFilterMethod = nameof(GetPossibleJsonObjectFlowBlocks),
            SelectionDisplayMember = nameof(Name))]
        public JsonObjectFlowBlock AssociatedJsonObject { get; set; }

        private List<JsonObjectFlowBlock> GetPossibleJsonObjectFlowBlocks()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetFlowBlocks<JsonObjectFlowBlock>()
                .ToList();
        }

        [Required]
        [Display(Name = "PropertyNames_EncodingName", Description = "PropertyNames_EncodingName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection)]
        public DotNetEncodingNames EncodingName { get; set; } = DotNetEncodingNames.Default;

        [Display(Name = "JsonObjectOutputFlowBlock_Indented", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool Indented { get; set; } = true;

        [Display(Name = "JsonObjectOutputFlowBlock_NullValueHandling", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Factory = UIFactory.ComboBox)]
        public NullValueHandling NullValueHandling { get; set; } = NullValueHandling.Include;

        [Display(Name = "JsonObjectOutputFlowBlock_DateFormatHandling", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(Factory = UIFactory.ComboBox)]
        public DateFormatHandling DateFormatHandling { get; set; } = DateFormatHandling.IsoDateFormat;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_braces, 16, SKColors.Orange);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_braces, 32, SKColors.Orange);

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Json;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(AssociatedJsonObject));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var associated = this.AssociatedJsonObject ?? GetPreviousFlowBlockOnPath<JsonObjectFlowBlock>(this);
                if (associated == null)
                    throw new InvalidOperationException("No JSON object source is assigned to the flow block.");

                if (associated.InternalJsonObject == null)
                    throw new InvalidOperationException("The JSON object in the source flow block is null or has not been initialized.");

                var jObject = associated.InternalJsonObject;

                var serializerSettings = new JsonSerializerSettings
                {
                    Formatting = Indented ? Formatting.Indented : Formatting.None,
                    NullValueHandling = this.NullValueHandling,
                    DateFormatHandling = this.DateFormatHandling
                };

                if (this.ResultField?.FieldType?.FieldType == FieldTypes.ByteArray)
                {
                    var encoding = EncodingName.ToEncoding();
                    using (var ms = new MemoryStream())
                    using (var sw = new StreamWriter(ms, encoding))
                    using (var jw = new JsonTextWriter(sw) { Formatting = serializerSettings.Formatting })
                    {
                        var serializer = JsonSerializer.Create(serializerSettings);
                        serializer.Serialize(jw, jObject);
                        jw.Flush();
                        sw.Flush();
                        GenerateResult(runtime, Convert.ToBase64String(ms.ToArray()));
                    }
                }
                else
                {
                    var json = JsonConvert.SerializeObject(jObject, serializerSettings);
                    GenerateResult(runtime, json);
                }
            });
        }
    }
}
