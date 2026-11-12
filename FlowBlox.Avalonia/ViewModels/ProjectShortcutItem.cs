using System.Windows.Input;
using Avalonia.Media.Imaging;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.Models.Project.Launcher;

namespace FlowBlox.AvaloniaDesktop.ViewModels;

public sealed class ProjectShortcutItem
{
    public ProjectShortcut Project { get; set; } = new();
    public ProjectExecutionEntry Execution { get; set; }
    public string Name => Project.Name;
    public bool IsLocal => !string.IsNullOrWhiteSpace(Project.ProjectFile);
    public bool IsOnline => !string.IsNullOrWhiteSpace(Project.ProjectSpaceGuid);
    public bool IsExecution => Execution != null;
    public bool ShowLocalIcon => IsLocal && !IsExecution;
    public bool ShowOnlineIcon => IsOnline && !IsExecution;
    public Bitmap ProjectIcon => ProjectShortcutIcons.Project;
    public Bitmap ExecutionIcon => ProjectShortcutIcons.Execution;
    public Bitmap OpenIcon => IsExecution ? ProjectShortcutIcons.ViewExecution : ProjectShortcutIcons.OpenProject;
    public bool IsRunning => Execution?.IsRunning == true;
    public bool IsFinished => Execution != null && !Execution.IsRunning;
    public string OutcomeColor => Execution?.Response?.Success == true ? "#75B96A" : "#E48372";
    public bool CanExecute => Execution == null;
    public double IconWidth { get; set; }
    public string Detail { get; set; }
    public string OpenText { get; set; }
    public string RunText { get; set; }
    public string RemoveText { get; set; }
    public ICommand OpenCommand { get; set; }
    public ICommand RunCommand { get; set; }
    public ICommand RemoveCommand { get; set; }
    public bool CanRemove => Execution != null;
}
