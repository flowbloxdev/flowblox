using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Util.Resources;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.UICore.ViewModels;

public sealed class ProjectInputConfigurationItem
{
    public ProjectInputConfiguration Configuration { get; }
    public string Name => Configuration.Name;
    public ProjectInputConfigurationStatus Status { get; }
    public bool HasMissingRequiredFields => Status.MissingRequiredFields.Count > 0;
    public string FilledSummary => string.Format(Text(nameof(Texts.ConfigurationFilled)), Status.FilledCount, Status.TotalCount);
    public string RequiredSummary => HasMissingRequiredFields
        ? string.Format(Text(nameof(Texts.ConfigurationMissing)), Status.MissingRequiredFields.Count)
        : Text(nameof(Texts.ConfigurationComplete));
    public string MissingFields => string.Join(", ", Status.MissingRequiredFields);

    public ProjectInputConfigurationItem(ProjectInputConfiguration configuration, IEnumerable<FieldElement> fields)
    {
        Configuration = configuration;
        Status = configuration.GetStatus(fields);
    }

    private static string Text(string key) => FlowBloxResourceUtil.GetLocalizedString(key, typeof(Texts));
}
