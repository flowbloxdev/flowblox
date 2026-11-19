using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;

namespace FlowBlox.Core.Util.Projects;

/// <summary>
/// Reads project definitions for editors, launchers and assistant tools without exposing current field values.
/// </summary>
public static class ProjectMetadataReader
{
    public static ProjectMetadata Read(FlowBloxProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new ProjectMetadata
        {
            ProjectName = project.ProjectName?.Trim(),
            ShortDescription = project.AssistantMetadata?.ShortDescription?.Trim() ?? string.Empty,
            Description = project.ProjectDescription?.Trim() ?? string.Empty,
            InputFields = ReadFields(project, UserFieldTypes.Input),
            MemoryFields = ReadFields(project, UserFieldTypes.Memory),
            Outputs = (project.FlowBlocks ?? [])
                .OfType<ProjectOutputFlowBlock>()
                .Where(output => !string.IsNullOrWhiteSpace(output.Name))
                .GroupBy(output => output.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group
                        .SelectMany(output => output.MappingEntries ?? [])
                        .Select(entry => entry.OutputPropertyName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<ProjectFieldMetadata> ReadFields(FlowBloxProject project, UserFieldTypes type) =>
        (project.UserFields ?? [])
            .Where(field => field.UserField && field.UserFieldType == type && !string.IsNullOrWhiteSpace(field.Name))
            .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => ReadField(group.First()))
            .ToList();

    private static ProjectFieldMetadata ReadField(FieldElement field) => new()
    {
        Name = field.Name,
        FieldType = field.FieldType?.FieldType ?? FieldTypes.Text,
        DefaultValue = field.IsUserInputField() && !field.IsPassword ? field.DefaultValue : null,
        IsPassword = field.IsPassword,
        IsRequiredAtRuntimeStartup = field.IsRequiredAtRuntimeStartup,
        LoVType = field.LoVType,
        ClearOnRuntimeStart = field.ClearOnRuntimeStart,
        SyntaxHighlighting = field.SyntaxHighlighting,
        ListOfValues = field.ListOfValues?.Select(value => value.Value).ToList() ?? []
    };
}
