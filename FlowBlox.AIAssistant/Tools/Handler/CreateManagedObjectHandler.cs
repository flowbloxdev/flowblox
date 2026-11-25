using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Create a managed object.")]
    internal sealed class CreateManagedObjectHandler : ToolHandlerBase
    {
        public override string Name => "CreateManagedObject";
        public override bool IsLayoutRelevantForAutoAdjustment => true;

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Creates managed objects except FieldElement. Use CreateField for FlowBlock result fields or CreateUserField for Input/Memory user fields.",
            new JObject
            {
                ["typeFullName"] = "string",
                ["name"] = "string?"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var type = ToolHandlerUtilities.ResolveType(args.Value<string>("typeFullName"));
            if (type == null
                || !typeof(IManagedObject).IsAssignableFrom(type)
                || typeof(BaseFlowBlock).IsAssignableFrom(type)
                || type.IsAbstract)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail("typeFullName invalid."));
            }

            if (typeof(FieldElement).IsAssignableFrom(type))
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    "FieldElement cannot be created with CreateManagedObject. Use CreateField with sourceFlowBlockName for a BaseResultFlowBlock result field, or CreateUserField for an Input/Memory user field."));
            }

            var managedObject = Activator.CreateInstance(type) as IManagedObject;
            if (managedObject == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail("Could not create managed object."));
            }

            var requestedName = args.Value<string>("name");
            if (managedObject is FlowBloxComponent component && !string.IsNullOrWhiteSpace(requestedName))
            {
                component.Name = requestedName;
            }

            var registry = ToolHandlerUtilities.GetRegistry();
            registry.PostProcessManagedObjectCreated(managedObject);
            registry.RegisterManagedObject(managedObject);

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["created"] = true,
                ["name"] = (managedObject as FlowBloxComponent)?.Name ?? string.Empty,
                ["typeFullName"] = managedObject.GetType().FullName
            }));
        }
    }
}