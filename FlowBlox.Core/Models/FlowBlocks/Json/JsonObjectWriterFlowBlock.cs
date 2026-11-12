using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Json
{
    [Display(Name = "JsonObjectWriterFlowBlock_DisplayName", Description = "JsonObjectWriterFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("JsonObjectWriterFlowBlock_SpecialExplanation_ExternalFlowBlocks", Icon = SpecialExplanationIcon.Information)]
    [FlowBloxSpecialExplanation("JsonFlowBlocks_SpecialExplanation_JPathSyntax", Icon = SpecialExplanationIcon.Information)]
    public class JsonObjectWriterFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
        public override string DefaultResultFieldName => GlobalConstants.SuccessFieldName;

        [Display(Name = "JsonObjectWriterFlowBlock_AssociatedJsonObject", Description = "JsonObjectWriterFlowBlock_AssociatedJsonObject_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [AssociatedFlowBlockResolvable()]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Link | UIOperations.Unlink,
            SelectionFilterMethod = nameof(GetPossibleJsonObjectFlowBlocks),
            SelectionDisplayMember = nameof(Name))]
        public JsonObjectFlowBlock AssociatedJsonObject { get; set; }

        public List<JsonObjectFlowBlock> GetPossibleJsonObjectFlowBlocks()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetFlowBlocks<JsonObjectFlowBlock>()
                .ToList();
        }

        [Display(Name = "JsonObjectWriterFlowBlock_AssociatedJsonObjectWriter", Description = "JsonObjectWriterFlowBlock_AssociatedJsonObjectWriter_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Link | UIOperations.Unlink,
           SelectionFilterMethod = nameof(GetPossibleJsonObjectWriters),
           SelectionDisplayMember = nameof(BaseFlowBlock.Name))]
        public JsonObjectWriterFlowBlock AssociatedJsonObjectWriter { get; set; }

        public List<JsonObjectWriterFlowBlock> GetPossibleJsonObjectWriters()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            return registry.GetFlowBlocks<JsonObjectWriterFlowBlock>().ToList();
        }

        [Display(Name = "JsonObjectWriterFlowBlock_JsonAssignments", Description = "JsonObjectWriterFlowBlock_JsonAssignments_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 10)]
        [FlowBloxUI(Factory = UIFactory.GridView, Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public ObservableCollection<JsonPropertyValueAssignment> Assignments { get; set; } = new();


        [Display(Name = "JsonObjectWriterFlowBlock_Path", Description = "JsonObjectWriterFlowBlock_Path_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.JPath), UiOptions = UIOptions.EnableFieldSelection)]
        public string Path { get; set; }

        [Display(Name = "JsonObjectWriterFlowBlock_IsArray", Description = "JsonObjectWriterFlowBlock_IsArray_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool IsArray { get; set; }

        [JsonIgnore]
        [DeepCopierIgnore]
        public JObject CreatedOrUpdatedObject { get; private set; }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_tags, 16, SKColors.Orange);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_tags, 32, SKColors.Orange);
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Json;
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(AssociatedJsonObject));
            properties.Add(nameof(AssociatedJsonObjectWriter));
            properties.Add(nameof(Path));
            properties.Add(nameof(IsArray));
            properties.Add(nameof(Assignments));
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

                // Determine root/parent
                JToken root = associated.InternalJsonObject;
                if (AssociatedJsonObjectWriter != null)
                {
                    if (AssociatedJsonObjectWriter.CreatedOrUpdatedObject == null)
                        throw new InvalidOperationException("SourceObject was specified, but no CreatedToken exists.");
                    root = AssociatedJsonObjectWriter.CreatedOrUpdatedObject;
                }

                var path = this.Resolve(owner => owner.Path);
                var effectivePath = string.IsNullOrWhiteSpace(path) ? "$" : path.Trim();
                var createdOrUpdatedObject = JPathEnsurer.EnsureObject(root, effectivePath, IsArray);

                CreatedOrUpdatedObject = createdOrUpdatedObject;
                ApplyAssignments(createdOrUpdatedObject);
                GenerateResult(runtime, bool.TrueString.ToLowerInvariant());
            });
        }

        private void ApplyAssignments(JObject jObject)
        {
            if (jObject == null)
                throw new ArgumentNullException(nameof(jObject));

            foreach (var a in Assignments)
            {
                var propertyName = a.Resolve(owner => owner.PropertyName);
                var valueObj = a.FieldValue != null
                    ? a.FieldValue.Value
                    : a.Resolve(owner => owner.Value);

                if (a.IsArray)
                {
                    if (!jObject.TryGetValue(propertyName, out var token) || token.Type != JTokenType.Array)
                    {
                        token = new JArray();
                        jObject[propertyName] = token;
                    }

                    if (token is JArray arr)
                        arr.Add(valueObj != null ? JToken.FromObject(valueObj) : JValue.CreateNull());
                    else
                        throw new InvalidOperationException($"Property '{propertyName}' exists but is not an array.");
                }
                else
                {
                    jObject[propertyName] = valueObj != null
                        ? JToken.FromObject(valueObj)
                        : JValue.CreateNull();
                }
            }
        }
    }
}
