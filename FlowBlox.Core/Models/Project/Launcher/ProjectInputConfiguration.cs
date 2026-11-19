using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using Newtonsoft.Json;

namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectInputConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ProjectShortcut Project { get; set; } = new();

    [JsonConverter(typeof(ProtectedRunnerRequestConverter))]
    public RunnerRequest Request { get; set; } = new();

    public string GetInputValue(FieldElement field)
    {
        var values = new Dictionary<string, string>(Request.UserFields, StringComparer.OrdinalIgnoreCase);
        var value = values.GetValueOrDefault(field.Name);
        return string.IsNullOrEmpty(value) ? (field.IsPassword ? string.Empty : field.DefaultValue ?? string.Empty) : value;
    }

    public ProjectInputConfigurationStatus GetStatus(IEnumerable<FieldElement> fields)
    {
        var inputs = fields.ToList();
        return new ProjectInputConfigurationStatus
        {
            TotalCount = inputs.Count,
            FilledCount = inputs.Count(field => !string.IsNullOrWhiteSpace(GetInputValue(field))),
            MissingRequiredFields = inputs
                .Where(field => field.IsRequiredAtRuntimeStartup && string.IsNullOrWhiteSpace(GetInputValue(field)))
                .Select(field => field.Name)
                .ToList()
        };
    }
}
