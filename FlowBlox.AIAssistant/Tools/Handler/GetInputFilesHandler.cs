using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Enums;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetInputFilesHandler : ToolHandlerBase
    {
        public override string Name => "GetInputFiles";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns project managed input files (key is relative to $Project::InputDirectory). Returns metadata only.",
            new JObject());

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var project = ToolHandlerUtilities.GetProject();
            var inputFiles = project.InputFiles ?? new List<FlowBloxInputFile>();

            var items = new JArray(
                inputFiles
                    .Where(x => x != null)
                    .OrderBy(x => NormalizeKey(x.RelativePath), StringComparer.OrdinalIgnoreCase)
                    .Select(ToTemplateObject));

            var result = new JObject
            {
                ["projectInputDirectory"] = project.ProjectInputDirectory ?? string.Empty,
                ["placeholderHint"] = FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Project, "InputDirectory"),
                ["commandPlaceholderHint"] = "$InputFile::Path",
                ["count"] = items.Count,
                ["inputFiles"] = items
            };

            return Task.FromResult(ToolHandlerUtilities.Ok(result));
        }

        private static JObject ToTemplateObject(FlowBloxInputFile inputFile)
        {
            var normalizedKey = NormalizeKey(inputFile.RelativePath);
            var bytes = inputFile.ContentBytes ?? Array.Empty<byte>();

            return new JObject
            {
                ["key"] = normalizedKey,
                ["relativePath"] = normalizedKey,
                ["syncMode"] = inputFile.SyncMode.ToString(),
                ["command"] = inputFile.Command ?? string.Empty,
                ["executeBeforeRuntime"] = inputFile.ExecuteBeforeRuntime,
                ["sizeBytes"] = bytes.LongLength,
                ["hasContent"] = bytes.LongLength > 0
            };
        }

        private static string NormalizeKey(string key)
        {
            return FlowBloxInputFileHelper.NormalizeRelativePath(key ?? string.Empty);
        }
    }
}



