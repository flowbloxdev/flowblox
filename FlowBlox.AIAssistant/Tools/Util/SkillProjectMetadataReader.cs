using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static class SkillProjectMetadataReader
    {
        public static SkillProjectMetadata Read(string projectFile)
        {
            return Read(SkillProjectSerializer.FromFile(projectFile), projectFile);
        }

        public static SkillProjectMetadata Read(FlowBloxProject project, string projectFile) =>
            new(
                project.ProjectName?.Trim() ?? Path.GetFileNameWithoutExtension(projectFile),
                project.AssistantMetadata?.ShortDescription?.Trim() ?? string.Empty,
                project.ProjectDescription?.Trim() ?? string.Empty,
                project.UserFields
                    .Where(x => x.UserFieldType == UserFieldTypes.Input)
                    .Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                project.FlowBlocks
                    .OfType<ProjectOutputFlowBlock>()
                    .SelectMany(x => x.MappingEntries)
                    .Select(x => x.OutputPropertyName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Path.GetFullPath(projectFile),
                project.UserFields
                    .Where(x => x.UserFieldType == UserFieldTypes.Input && !string.IsNullOrWhiteSpace(x.Name))
                    .Where(x => x.ListOfValues?.Count > 0)
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => (IReadOnlyList<string>)x.First().ListOfValues.Select(value => value.Value).ToList(),
                        StringComparer.OrdinalIgnoreCase),
                ReadFieldMetadata(project, UserFieldTypes.Input),
                ReadFieldMetadata(project, UserFieldTypes.Memory));

        private static IReadOnlyDictionary<string, SkillFieldMetadata> ReadFieldMetadata(
            FlowBloxProject project,
            UserFieldTypes fieldType) =>
            project.UserFields
                .Where(x => x.UserFieldType == fieldType && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => new SkillFieldMetadata(
                        x.First().IsRequiredAtRuntimeStartup,
                        x.First().LoVType,
                        x.First().ClearOnRuntimeStart,
                        x.First().IsPassword,
                        x.First().SyntaxHighlighting),
                    StringComparer.OrdinalIgnoreCase);
    }

    internal sealed record SkillFieldMetadata(
        bool IsRequiredAtRuntimeStartup,
        LoVType LoVType,
        bool ClearOnRuntimeStart,
        bool IsPassword,
        string? SyntaxHighlighting);

    internal sealed record SkillProjectMetadata(
        string ProjectName,
        string ShortDescription,
        string Description,
        IReadOnlyList<string> InputFields,
        IReadOnlyList<string> OutputFields,
        string ProjectFile,
        IReadOnlyDictionary<string, IReadOnlyList<string>> InputFieldListOfValues,
        IReadOnlyDictionary<string, SkillFieldMetadata> InputFieldMetadata,
        IReadOnlyDictionary<string, SkillFieldMetadata> MemoryFieldMetadata);
}
