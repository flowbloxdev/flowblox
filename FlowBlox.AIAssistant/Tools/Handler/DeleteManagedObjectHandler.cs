using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Base;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Delete a managed object and handle references.")]
    internal sealed class DeleteManagedObjectHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "DeleteManagedObject";
        public override bool IsLayoutRelevantForAutoAdjustment => true;

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Deletes a managed object and automatically handles dependent references.",
            new JObject
            {
                ["name"] = "string",
                ["usageHint"] = "result.deletionReport is present only when dependencies were handled because of this deletion; absence means there were none. reviewStatus describes whether an entry is informational, should be reviewed, or requires manual follow-up. Entries with automaticallyRemoved=false require manual correction."
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var registry = ToolHandlerUtilities.GetRegistry();
            var name = args.Value<string>("name");

            var managedObject = registry.GetManagedObjects()
                .OfType<FlowBloxComponent>()
                .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

            if (managedObject == null)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail($"Managed object '{name}' was not found."));
            }

            var report = FlowBloxDeletionHelper.Analyze(registry, managedObject);
            var action = FlowBloxDeletionHelper.CreateAction(registry, report);
            action.Invoke();
            ToolHandlerUtilities.RegisterDeletionHistory(action);

            var result = new JObject
            {
                ["deleted"] = true,
                ["name"] = name
            };
            ToolHandlerUtilities.AddDeletionReportIfAny(result, report);
            return Task.FromResult(ToolHandlerUtilities.Ok(result));
        }
    }
}
