using FlowBlox.Core;
using FlowBlox.Core.Models;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Services;

internal static class AssistantActivityFormatter
{
    private const string GeneralActivityKey = "AiAssistantActivity_General";

    public static string Format(IReadOnlyList<AiResponseToolCall> calls)
    {
        var activityKeys = new List<string>();
        foreach (var call in calls)
        {
            var requests = call.Arguments["requests"] as JArray;
            if (call.ToolName.Equals("BatchExecuteToolRequests", StringComparison.OrdinalIgnoreCase) && requests != null)
            {
                foreach (var request in requests.OfType<JObject>())
                    activityKeys.Add(GetActivityKey(request.Value<string>("toolName") ?? string.Empty));
            }
            else
            {
                activityKeys.Add(GetActivityKey(call.ToolName));
            }
        }

        var distinctKeys = activityKeys.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctKeys.Length == 0)
            distinctKeys = [GeneralActivityKey];

        var messages = distinctKeys.Take(3)
            .Select(key => FlowBloxResourceUtil.GetLocalizedString(key, typeof(FlowBloxTexts)))
            .ToList();
        if (distinctKeys.Length > 3)
            messages.Add(FlowBloxResourceUtil.GetLocalizedString("AiAssistantActivity_Remaining", typeof(FlowBloxTexts)));

        return string.Join(" ", messages);
    }

    private static string GetActivityKey(string name)
    {
        if (name.Equals("LoadExplanation", StringComparison.OrdinalIgnoreCase))
            return "AiAssistantActivity_Guidance";

        if (name.Equals("ExecuteExternalProject", StringComparison.OrdinalIgnoreCase))
            return "AiAssistantActivity_Skill";

        if (name.StartsWith("Search", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("GetCategoryChildren", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("GetSkillProjects", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("GetDistributedData", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("GetToolboxEntries", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("GetPSProjects", StringComparison.OrdinalIgnoreCase))
        {
            return "AiAssistantActivity_Search";
        }

        if (name.StartsWith("Inspect", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("GetLastDebugArtefact", StringComparison.OrdinalIgnoreCase))
        {
            return "AiAssistantActivity_Results";
        }

        if (name.StartsWith("Run", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ExecuteInputFileCommand", StringComparison.OrdinalIgnoreCase))
        {
            return "AiAssistantActivity_Execute";
        }

        if (name.StartsWith("Create", StringComparison.OrdinalIgnoreCase))
            return "AiAssistantActivity_Create";

        if (name.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Set", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("SkillProjectEditing", StringComparison.OrdinalIgnoreCase))
        {
            return "AiAssistantActivity_Configure";
        }

        if (name.StartsWith("Delete", StringComparison.OrdinalIgnoreCase))
            return "AiAssistantActivity_Remove";

        if (name.Equals("ConnectFlowBlocks", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("DisconnectFlowBlocks", StringComparison.OrdinalIgnoreCase))
        {
            return "AiAssistantActivity_Connections";
        }

        if (name.StartsWith("Get", StringComparison.OrdinalIgnoreCase))
            return "AiAssistantActivity_Inspect";

        return GeneralActivityKey;
    }
}
