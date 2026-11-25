using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Read a generated output file.")]
    internal sealed class GetOutputFileContentHandler : ToolHandlerBase
    {
        public override string Name => "GetOutputFileContent";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns output file content for one key (relative to $Project::OutputDirectory). Text formats, XLSX, and DOCX are converted for reading; other binary content falls back to bounded Base64.",
            new JObject
            {
                ["key"] = "string (relative path under $Project::OutputDirectory)",
                ["textConversion"] = "bool? (default=true; false returns bounded Base64 without conversion)",
                ["maxTokens"] = "int? (default/maximum: configured MaxFieldValuesTokensPerResponse)",
                ["usageHint"] = "Returns textContent when possible. XLSX becomes CSV; DOCX becomes page-oriented text with header/body/footer."
            });

        public override async Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            try
            {
                var key = (args.Value<string>("key") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key))
                    return ToolHandlerUtilities.Fail("key is required.");

                var project = ToolHandlerUtilities.GetProject();
                var normalizedKey = FlowBloxInputFileHelper.NormalizeRelativePath(key);
                var filePath = ContainedFileResolver.ResolveExistingFile(project.ProjectOutputDirectory, key);
                var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
                if (!FieldValueResponseLimiter.TryFromConfiguration(
                        args.Value<int?>("maxTokens"),
                        out var limiter,
                        out var limiterError))
                    return ToolHandlerUtilities.Fail(limiterError);
                return ToolHandlerUtilities.Ok(
                    FileContentResponseBuilder.Build(
                        bytes,
                        normalizedKey,
                        "Output file",
                        filePath,
                        args.Value<bool?>("textConversion") ?? true,
                        limiter!));
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    "Failed to read AI Assistant output file content.",
                    ex);
                return ToolHandlerUtilities.Fail(ex.Message);
            }
        }
    }
}
