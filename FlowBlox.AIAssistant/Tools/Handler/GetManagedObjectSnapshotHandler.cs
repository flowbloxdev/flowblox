using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Base;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Inspect a managed object snapshot.")]
    internal sealed class GetManagedObjectSnapshotHandler : ToolHandlerBase
    {
        public override string Name => "GetManagedObjectSnapshot";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns managed object snapshot with refs normalized to names.",
            new JObject
            {
                ["name"] = "string"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var name = args.Value<string>("name");
            var managedObject = ToolHandlerUtilities.GetRegistry()
                .GetManagedObjects()
                .OfType<FlowBloxComponent>()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

            if (managedObject == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail($"Managed object '{name}' was not found."));
            }

            var snapshot = ToolHandlerUtilities.CreateSnapshot(
                "managedObject",
                managedObject.Name,
                managedObject.GetType().FullName,
                managedObject,
                null);

            return Task.FromResult(ToolHandlerUtilities.Ok(snapshot));
        }
    }
}
