using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Enums;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Project;
using Newtonsoft.Json.Linq;
using System.Text;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class CreateOrUpdateInputFileHandler : ToolHandlerBase
    {
        private static readonly string[] SupportedConverters =
        [
            Csv2XlsxConverter.Name,
            Csv2XlsConverter.Name
        ];

        public override string Name => "CreateOrUpdateInputFile";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Creates or updates a managed input file from generated text or distributed FlowBlox data.",
            new JObject
            {
                ["key"] = "string (relative path under $Project::InputDirectory)",
                ["generatedTemplate"] = "object? ({ textContent: string, converter?: string })",
                ["copyFromDistributedData"] = "object? ({ dataType: string, fileName: string }); use exactly one content source",
                ["supportedConverters"] = new JArray(SupportedConverters),
                ["syncMode"] = "string? (CreateIfNotExists|AlwaysOverwrite, default: CreateIfNotExists)",
                ["command"] = "string? (optional command; can use $InputFile::Path)",
                ["executeBeforeRuntime"] = "bool? (optional; execute command before runtime start)",
                ["usageHint"] =
                    "Use generatedTemplate or copyFromDistributedData as the content source. " +
                    "Set generatedTemplate.converter='Csv2XlsxConverter' or 'Csv2XlsConverter' to convert CSV text to Excel. " +
                    "Set command for on-demand execution through ExecuteInputFileCommand; add executeBeforeRuntime only for automatic execution scenarios. No attachments."
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

                var generatedTemplate = args["generatedTemplate"] as JObject;
                var distributedData = args["copyFromDistributedData"] as JObject;
                if ((generatedTemplate == null) == (distributedData == null))
                {
                    return Task.FromResult(ToolHandlerUtilities.Fail(
                        "Provide exactly one content source: generatedTemplate or copyFromDistributedData."));
                }

                var converter = (generatedTemplate?.Value<string>("converter") ?? string.Empty).Trim();
                string textContent;
                string contentSource;
                if (generatedTemplate != null)
                {
                    textContent = generatedTemplate.Value<string>("textContent") ?? string.Empty;
                    contentSource = "generatedTemplate";
                }
                else
                {
                    var distributedDataCopy = DistributedDataCopyResolver.Resolve(distributedData!);
                    textContent = distributedDataCopy.Content;
                    contentSource = distributedDataCopy.Source;
                }

                var contentBytes = ConvertContent(textContent, converter);
                var contentBase64 = Convert.ToBase64String(contentBytes);

                var syncMode = ParseSyncMode(args.Value<string>("syncMode"));
                var existing = project.InputFiles.FirstOrDefault(x =>
                    string.Equals(
                        FlowBloxInputFileHelper.NormalizeRelativePath(x?.RelativePath ?? string.Empty),
                        normalizedKey,
                        StringComparison.OrdinalIgnoreCase));

                var created = existing == null;
                var inputFile = existing ?? new FlowBloxInputFile();
                inputFile.RelativePath = normalizedKey;
                inputFile.ContentBase64 = contentBase64;
                inputFile.SyncMode = syncMode;
                inputFile.Command = args.Value<string>("command") ?? inputFile.Command;
                inputFile.ExecuteBeforeRuntime = args.Value<bool?>("executeBeforeRuntime") ?? inputFile.ExecuteBeforeRuntime;

                if (created)
                    project.InputFiles.Add(inputFile);

                FlowBloxInputFileHelper.EnsureInputFilesExist(project);

                var materializedPath = FlowBloxInputFileHelper.BuildAbsoluteTargetPath(
                    project.ProjectInputDirectory,
                    normalizedKey);

                var payload = new JObject
                {
                    ["created"] = created,
                    ["updated"] = !created,
                    ["key"] = normalizedKey,
                    ["syncMode"] = syncMode.ToString(),
                    ["contentSource"] = contentSource,
                    ["converterUsed"] = string.IsNullOrWhiteSpace(converter) ? "None" : converter,
                    ["supportedConverters"] = new JArray(SupportedConverters),
                    ["sizeBytes"] = inputFile.ContentBytes?.LongLength ?? 0,
                    ["command"] = inputFile.Command ?? string.Empty,
                    ["executeBeforeRuntime"] = inputFile.ExecuteBeforeRuntime,
                    ["placeholderHint"] = FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.InputFile, "Path"),
                    ["materializedPath"] = materializedPath,
                    ["projectInputDirectory"] = project.ProjectInputDirectory ?? string.Empty
                };

                return Task.FromResult(ToolHandlerUtilities.Ok(payload));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }

        private static byte[] ConvertContent(string textContent, string converter)
        {
            if (string.IsNullOrWhiteSpace(converter))
                return Encoding.UTF8.GetBytes(textContent ?? string.Empty);

            if (string.Equals(converter, Csv2XlsxConverter.Name, StringComparison.OrdinalIgnoreCase))
                return Csv2XlsxConverter.Convert(textContent ?? string.Empty);

            if (string.Equals(converter, Csv2XlsConverter.Name, StringComparison.OrdinalIgnoreCase))
                return Csv2XlsConverter.Convert(textContent ?? string.Empty);

            throw new InvalidOperationException(
                $"Unsupported converter '{converter}'. Supported converters: {string.Join(", ", SupportedConverters)}.");
        }

        private static FlowBloxInputFileSyncMode ParseSyncMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return FlowBloxInputFileSyncMode.CreateIfNotExists;

            if (Enum.TryParse<FlowBloxInputFileSyncMode>(value.Trim(), true, out var parsed))
                return parsed;

            return FlowBloxInputFileSyncMode.CreateIfNotExists;
        }
    }
}



