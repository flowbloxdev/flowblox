using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetComponentSnapshotHandler : ToolHandlerBase
    {
        public override string Name => "GetComponentSnapshot";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns compact component JSON, or filters complete JSON including null/default values. Invalid/unmatched paths fail.",
            new JObject
            {
                ["kind"] = "string (flowBlock|managedObject|field)",
                ["name"] = "string",
                ["jsonFilter"] = "string[]? (absolute JPaths, e.g. [\"$.Name\",\"$.MappingEntries\"]; omit for compact JSON)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var kind = args.Value<string>("kind")?.Trim();
            var name = args.Value<string>("name")?.Trim();

            if (string.IsNullOrWhiteSpace(kind))
                return Task.FromResult(ToolHandlerUtilities.Fail("kind is required."));
            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult(ToolHandlerUtilities.Fail("name is required."));

            var registry = ToolHandlerUtilities.GetRegistry();
            FlowBloxReactiveObject? component = null;
            var normalizedKind = kind.ToLowerInvariant();
            string componentName;

            switch (normalizedKind)
            {
                case "flowblock":
                    component = ToolHandlerUtilities.ResolveFlowBlockByName(registry, name);
                    componentName = (component as BaseFlowBlock)?.Name ?? name;
                    break;
                case "managedobject":
                    component = ToolHandlerUtilities.ResolveManagedObjectByName(registry, name) as FlowBloxReactiveObject;
                    componentName = (component as IManagedObject)?.Name ?? name;
                    break;
                case "field":
                    component = ToolHandlerUtilities.ResolveFieldElementByFQName(registry, name);
                    componentName = (component as FieldElement)?.FullyQualifiedName ?? name;
                    break;
                default:
                    return Task.FromResult(ToolHandlerUtilities.Fail("kind must be 'flowBlock', 'managedObject', or 'field'."));
            }

            if (component == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail($"{kind} '{name}' was not found."));
            }

            var hasFilter = args.TryGetValue("jsonFilter", out var jsonFilter);
            var snapshotJson = JsonConvert.SerializeObject(component, JsonSettings.ComponentSnapshotExport(compact: !hasFilter));
            var snapshotToken = JToken.Parse(snapshotJson);

            var result = new JObject
            {
                ["kind"] = normalizedKind,
                ["name"] = componentName,
                ["typeFullName"] = component.GetType().FullName
            };
            if (hasFilter)
            {
                try
                {
                    result["matches"] = SnapshotJsonFilter.Select(snapshotToken, jsonFilter);
                }
                catch (ArgumentException exception)
                {
                    return Task.FromResult(ToolHandlerUtilities.Fail(exception.Message));
                }
            }
            else
            {
                result["snapshot"] = snapshotToken;
            }

            return Task.FromResult(ToolHandlerUtilities.Ok(result));
        }
    }
}
