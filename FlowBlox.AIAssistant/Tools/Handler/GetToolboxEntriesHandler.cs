using FlowBlox.Core.Constants;
using System.IO;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class GetToolboxEntriesHandler : ToolHandlerBase
    {
        public override string Name => "GetToolboxEntries";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns toolbox templates/examples for a toolbox category (e.g. Regex, XPath, DBConnection). Use when unsure about a property value and a property exposes a toolboxCategory in type metadata.",
            new JObject
            {
                ["toolboxCategory"] = "string",
                ["includeContent"] = "bool? (default=true)",
                ["maxEntries"] = "int? (default=200)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var toolboxCategory = args.Value<string>("toolboxCategory")?.Trim();
            if (string.IsNullOrWhiteSpace(toolboxCategory))
                return Task.FromResult(ToolHandlerUtilities.Fail("toolboxCategory is required."));

            var includeContent = args.Value<bool?>("includeContent") ?? true;
            var maxEntries = Math.Clamp(args.Value<int?>("maxEntries") ?? 200, 1, 1000);

            var options = FlowBloxOptions.GetOptionInstance();
            var toolboxUserFile = options.GetOption(OptionsConstants.PathsToolboxUserFile)?.Value ?? string.Empty;
            var toolboxDirectory = options.GetOption(OptionsConstants.PathsToolboxDir)?.Value ?? string.Empty;
            var toolboxCacheDirectory = options.GetOption(OptionsConstants.PathsToolboxCacheDir)?.Value ?? string.Empty;

            var files = CollectToolboxFiles(toolboxUserFile, toolboxDirectory, toolboxCacheDirectory);
            var entries = new List<JObject>();
            var normalizedCategory = toolboxCategory.Trim();

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(file))
                    continue;

                try
                {
                    var content = File.ReadAllText(file);
                    var root = JObject.Parse(content);
                    var toolboxElements = root["ToolboxElements"] as JArray;
                    if (toolboxElements == null)
                        continue;

                    foreach (var token in toolboxElements.OfType<JObject>())
                    {
                        var category = token.Value<string>("ToolboxCategory") ?? string.Empty;
                        if (!string.Equals(category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var name = token.Value<string>("Name") ?? string.Empty;
                        var description = token.Value<string>("Description") ?? string.Empty;
                        var entryContent = token.Value<string>("Content") ?? string.Empty;

                        var sourceScope = ResolveSourceScope(file, toolboxUserFile, toolboxDirectory, toolboxCacheDirectory);
                        var isEditable = string.Equals(
                            NormalizePath(file),
                            NormalizePath(toolboxUserFile),
                            StringComparison.OrdinalIgnoreCase);

                        var entry = new JObject
                        {
                            ["toolboxCategory"] = category,
                            ["toolboxCategoryDisplayName"] = FlowBloxToolboxCategory.GetDisplayName(category),
                            ["name"] = name,
                            ["description"] = description,
                            ["sourceFile"] = file,
                            ["sourceFileName"] = Path.GetFileName(file),
                            ["sourceScope"] = sourceScope,
                            ["isEditable"] = isEditable
                        };

                        if (includeContent)
                            entry["content"] = entryContent;

                        entries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        $"Failed to read toolbox entry file '{file}'.",
                        ex);
                }
            }

            var ordered = entries
                .OrderBy(x => x.Value<string>("name"), StringComparer.OrdinalIgnoreCase)
                .Take(maxEntries)
                .ToList();

            var result = new JObject
            {
                ["toolboxCategory"] = normalizedCategory,
                ["toolboxCategoryDisplayName"] = FlowBloxToolboxCategory.GetDisplayName(normalizedCategory),
                ["count"] = ordered.Count,
                ["includeContent"] = includeContent,
                ["maxEntries"] = maxEntries,
                ["entries"] = new JArray(ordered),
                ["hint"] = "Toolbox entries are templates/examples. Use them to bootstrap unsure property values."
            };

            return Task.FromResult(ToolHandlerUtilities.Ok(result));
        }

        private static List<string> CollectToolboxFiles(string toolboxUserFile, string toolboxDirectory, string toolboxCacheDirectory)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(toolboxDirectory) && Directory.Exists(toolboxDirectory))
            {
                foreach (var file in Directory.GetFiles(toolboxDirectory, "*.json"))
                    files.Add(file);
            }

            if (!string.IsNullOrWhiteSpace(toolboxUserFile))
                files.Add(toolboxUserFile);

            if (!string.IsNullOrWhiteSpace(toolboxCacheDirectory) && Directory.Exists(toolboxCacheDirectory))
            {
                foreach (var file in Directory.GetFiles(toolboxCacheDirectory, "*.json"))
                    files.Add(file);
            }

            return files.ToList();
        }

        private static string ResolveSourceScope(string file, string toolboxUserFile, string toolboxDirectory, string toolboxCacheDirectory)
        {
            var normalizedFile = NormalizePath(file);
            var normalizedUserFile = NormalizePath(toolboxUserFile);
            var normalizedToolboxDirectory = NormalizePath(toolboxDirectory);
            var normalizedToolboxCacheDirectory = NormalizePath(toolboxCacheDirectory);

            if (!string.IsNullOrWhiteSpace(normalizedUserFile) &&
                string.Equals(normalizedFile, normalizedUserFile, StringComparison.OrdinalIgnoreCase))
            {
                return "User";
            }

            if (!string.IsNullOrWhiteSpace(normalizedToolboxDirectory) &&
                normalizedFile.StartsWith(normalizedToolboxDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return "ToolboxDirectory";
            }

            if (!string.IsNullOrWhiteSpace(normalizedToolboxCacheDirectory) &&
                normalizedFile.StartsWith(normalizedToolboxCacheDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return "GlobalCache";
            }

            return "Unknown";
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return Path.GetFullPath(path.Trim());
        }
    }
}
