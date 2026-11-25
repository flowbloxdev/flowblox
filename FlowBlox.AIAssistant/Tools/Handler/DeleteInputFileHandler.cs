using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Delete a managed input file.")]
    internal sealed class DeleteInputFileHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "DeleteInputFile";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Deletes a managed input file entry by key (relative to $Project::InputDirectory).",
            new JObject
            {
                ["key"] = "string (relative path under $Project::InputDirectory)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            try
            {
                var project = ToolHandlerUtilities.GetProject();
                project.InputFiles ??= new List<FlowBloxInputFile>();

                var key = (args.Value<string>("key") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key))
                    return Task.FromResult(ToolHandlerUtilities.Fail("key is required."));

                FlowBloxInputFileHelper.ValidateRelativePathOrThrow(key);
                var normalizedKey = FlowBloxInputFileHelper.NormalizeRelativePath(key);

                var existing = project.InputFiles.FirstOrDefault(x =>
                    string.Equals(
                        FlowBloxInputFileHelper.NormalizeRelativePath(x?.RelativePath ?? string.Empty),
                        normalizedKey,
                        StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                    return Task.FromResult(ToolHandlerUtilities.Fail($"Input file '{normalizedKey}' was not found."));

                project.InputFiles.Remove(existing);

                // Keep behavior aligned with project-load synchronization path.
                FlowBloxInputFileHelper.EnsureInputFilesExist(project);

                return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
                {
                    ["deleted"] = true,
                    ["key"] = normalizedKey,
                    ["projectInputDirectory"] = project.ProjectInputDirectory ?? string.Empty,
                    ["note"] = "Existing file on disk is not removed automatically; only the managed input file entry is deleted."
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }
    }
}



