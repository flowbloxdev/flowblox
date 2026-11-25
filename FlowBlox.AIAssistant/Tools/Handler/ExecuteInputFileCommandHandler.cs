using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Run a managed input file command with approval.")]
    internal sealed class ExecuteInputFileCommandHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "ExecuteInputFileCommand";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Materializes the selected managed input file according to its sync mode and then executes its configured command in $Project::InputDirectory. " +
            "The managed script is project-specific; the command may write cross-project artifacts to a separate, feature-specific shared root such as an ONNX ModelRootDirectory. " +
            "No separate file synchronization or materialization call is required. " +
            "Script creation/update is always allowed. Call execution directly; the AI Assistant UI requests user confirmation automatically.",
            new JObject
            {
                ["key"] = "string (required, input file relative path under $Project::InputDirectory)",
                ["usageHint"] =
                    "Use this only for input-file commands configured via CreateOrUpdateInputFile. " +
                    "The following placeholders in command are resolved: $InputFile::Path, $InputFile::RelativePath"
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

                var inputFile = project.InputFiles.FirstOrDefault(x =>
                    string.Equals(
                        FlowBloxInputFileHelper.NormalizeRelativePath(x?.RelativePath ?? string.Empty),
                        normalizedKey,
                        StringComparison.OrdinalIgnoreCase));

                if (inputFile == null)
                    return Task.FromResult(ToolHandlerUtilities.Fail($"Input file '{normalizedKey}' was not found."));

                var rawCommand = inputFile.Command ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawCommand))
                    return Task.FromResult(ToolHandlerUtilities.Fail($"Input file '{normalizedKey}' has no command configured."));

                var result = FlowBloxInputFileCommandExecutor.Execute(project, inputFile, ct);

                var payload = new JObject
                {
                    ["key"] = normalizedKey,
                    ["workingDirectory"] = project.ProjectInputDirectory ?? string.Empty,
                    ["command"] = result.Command,
                    ["success"] = result.Success,
                    ["exitCode"] = result.ExitCode,
                    ["standardOutput"] = result.StandardOutput ?? string.Empty,
                    ["standardError"] = result.StandardError ?? string.Empty,
                    ["exceptionMessage"] = result.ExceptionMessage ?? string.Empty
                };

                if (!result.Success)
                {
                    var error = !string.IsNullOrWhiteSpace(result.ExceptionMessage)
                        ? result.ExceptionMessage
                        : $"Command failed for '{normalizedKey}' (ExitCode={result.ExitCode}).";

                    return Task.FromResult(ToolHandlerUtilities.Fail(error, payload));
                }

                return Task.FromResult(ToolHandlerUtilities.Ok(payload));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }
    }
}
