using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Util.Resources;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;
using System.ComponentModel;

namespace FlowBlox.UICore.ViewModels;

public sealed class ProjectExecutionHistoryItem : INotifyPropertyChanged
{
    public ProjectExecutionEntry Entry { get; private set; }

    public string ProjectName => Entry.Project.Name;
    public bool IsRunning => Entry.IsRunning;
    public bool IsSuccessful => !IsRunning && Entry.Response?.Success == true;
    public bool HasFailed => !IsRunning && !IsSuccessful;
    public string Status => FlowBloxResourceUtil.GetLocalizedString(
        IsRunning ? nameof(Texts.Running) : IsSuccessful ? nameof(Texts.Succeeded) : nameof(Texts.Failed),
        typeof(Texts));

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Timestamp => Entry.StartedUtc.ToLocalTime().ToString("g");

    public string DisplayName => string.Format(
        FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.HistoryExecution), typeof(Texts)),
        Entry.StartedUtc.ToLocalTime());

    public ProjectExecutionHistoryItem(ProjectExecutionEntry entry)
    {
        Entry = entry;
    }

    public void Update(ProjectExecutionEntry entry)
    {
        Entry = entry;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
