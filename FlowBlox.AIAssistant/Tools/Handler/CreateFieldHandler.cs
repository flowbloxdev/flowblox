using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Base;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Create a FlowBlock result field.", IncludeContractInBootstrap = true)]
    internal sealed class CreateFieldHandler : ToolHandlerBase
    {
        public override string Name => "CreateField";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Creates a field for a BaseResultFlowBlock and returns field reference metadata.",
            new JObject
            {
                ["sourceFlowBlockName"] = "string",
                ["fieldType"] = "FieldTypes? (default Text)",
                ["nameGenerationMode"] = "FieldNameGenerationMode? (default UseFallbackIndexOnly)",
                ["overrideName"] = "string?",
                ["usageHint"] = "Before CreateField, call GetComponentSnapshot and create only missing fields. BaseSingleResultFlowBlock (incl. BasePipeFlowBlock) normally creates ResultField automatically; if you notice a missing ResultField, repair it with CreateField using sourceFlowBlockName."
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var registry = ToolHandlerUtilities.GetRegistry();
            var sourceFlowBlockName = args.Value<string>("sourceFlowBlockName");

            var source = registry.GetFlowBlocks()
                .OfType<BaseResultFlowBlock>()
                .FirstOrDefault(x => string.Equals(x.Name, sourceFlowBlockName, StringComparison.OrdinalIgnoreCase));

            if (source == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"BaseResultFlowBlock '{sourceFlowBlockName}' was not found."));
            }

            var singleResultSource = source as BaseSingleResultFlowBlock;
            if (singleResultSource != null && singleResultSource.ResultField != null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Flow block '{source.Name}' already has a ResultField. Reuse or update it instead of creating another field."));
            }

            var fieldTypeValue = args.Value<string>("fieldType");
            var fieldType = ParseEnum(fieldTypeValue, singleResultSource?.DefaultResultFieldType ?? FieldTypes.Text);

            var modeValue = args.Value<string>("nameGenerationMode");
            var nameGenerationMode = ParseEnum(modeValue, singleResultSource != null
                ? FieldNameGenerationMode.DeriveFromFlowBlock
                : FieldNameGenerationMode.UseFallbackIndexOnly);

            var field = registry.CreateField(source, nameGenerationMode, fieldType);

            var overrideName = args.Value<string>("overrideName");
            if (string.IsNullOrWhiteSpace(overrideName))
                overrideName = singleResultSource?.DefaultResultFieldName;

            if (!string.IsNullOrWhiteSpace(overrideName))
            {
                field.Name = overrideName;
            }

            if (singleResultSource != null)
                singleResultSource.ResultField = field;

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["created"] = true,
                ["name"] = field.Name,
                ["fullyQualifiedFieldName"] = field.FullyQualifiedName,
                ["sourceFlowBlockName"] = source.Name,
                ["fieldType"] = field.FieldType?.FieldType.ToString() ?? string.Empty,
                ["typeFullName"] = field.GetType().FullName,
                ["resolver"] = new JObject
                {
                    ["resolveFieldElementByFQName"] = field.FullyQualifiedName
                }
            }));
        }

        private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct
        {
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<TEnum>(value, true, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }
}