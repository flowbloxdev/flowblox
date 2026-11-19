using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.Projects;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static class SkillProjectMetadataReader
    {
        public static SkillProjectMetadata Read(string projectFile)
        {
            return Read(SkillProjectSerializer.FromFile(projectFile), projectFile);
        }

        public static SkillProjectMetadata Read(FlowBloxProject project, string? projectFile)
        {
            var metadata = ProjectMetadataReader.Read(project);
            return new SkillProjectMetadata(
                metadata.ProjectName ?? Path.GetFileNameWithoutExtension(projectFile),
                metadata.ShortDescription,
                metadata.Description,
                metadata.InputFields
                    .Select(field => field.Name)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                metadata.Outputs.Values
                    .SelectMany(fields => fields)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                projectFile == null ? string.Empty : Path.GetFullPath(projectFile),
                metadata.InputFields
                    .Where(field => field.ListOfValues.Count > 0)
                    .ToDictionary(
                        field => field.Name,
                        field => field.ListOfValues,
                        StringComparer.OrdinalIgnoreCase),
                metadata.InputFields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase),
                metadata.MemoryFields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase));
        }
    }

    internal sealed record SkillProjectMetadata(
        string ProjectName,
        string ShortDescription,
        string Description,
        IReadOnlyList<string> InputFields,
        IReadOnlyList<string> OutputFields,
        string ProjectFile,
        IReadOnlyDictionary<string, IReadOnlyList<string>> InputFieldListOfValues,
        IReadOnlyDictionary<string, ProjectFieldMetadata> InputFieldMetadata,
        IReadOnlyDictionary<string, ProjectFieldMetadata> MemoryFieldMetadata);
}
