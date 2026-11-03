using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using FlowBlox.Core.Models.Project;
using FlowBlox.UICore.Commands;
using ICSharpCode.AvalonEdit.Document;
using Texts = FlowBlox.UICore.Resources.CreateEditProjectWindow;

namespace FlowBlox.UICore.ViewModels;

public sealed class CreateEditProjectViewModel : INotifyPropertyChanged
{
    private readonly FlowBloxProject _project;
    private readonly Action<bool> _close;
    private string _projectName;
    private string _errorText = string.Empty;

    public CreateEditProjectViewModel(FlowBloxProject project, bool isEditing, Action<bool> close)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _close = close;
        Title = isEditing ? Texts.EditTitle : Texts.CreateTitle;
        _projectName = project.ProjectName ?? string.Empty;
        Author = project.Author ?? string.Empty;
        Description = project.ProjectDescription ?? string.Empty;
        ShortDescription = project.AssistantMetadata.ShortDescription ?? string.Empty;
        Notice = project.Notice ?? string.Empty;
        ApplyCommand = new RelayCommand(Apply);
        CancelCommand = new RelayCommand(() => _close(false));
        CreateInputDirectoryCommand = new RelayCommand(() => CreateDirectory(InputDirectory), () => CanCreateInputDirectory);
        CreateOutputDirectoryCommand = new RelayCommand(() => CreateDirectory(OutputDirectory), () => CanCreateOutputDirectory);
        OpenInputDirectoryCommand = new RelayCommand(() => OpenDirectory(InputDirectory), () => Directory.Exists(InputDirectory));
        OpenOutputDirectoryCommand = new RelayCommand(() => OpenDirectory(OutputDirectory), () => Directory.Exists(OutputDirectory));
    }

    public string Title { get; }
    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (_projectName == value) return;
            _projectName = value;
            ErrorText = string.Empty;
            OnPropertyChanged();
            RefreshDirectories();
        }
    }

    public string Author { get; set; }
    public TextDocument DescriptionDocument { get; } = new();
    public TextDocument NoticeDocument { get; } = new();
    public string Description
    {
        get => DescriptionDocument.Text;
        set => DescriptionDocument.Text = value ?? string.Empty;
    }
    public string ShortDescription { get; set; }
    public string Notice
    {
        get => NoticeDocument.Text;
        set => NoticeDocument.Text = value ?? string.Empty;
    }
    public string InputDirectory => _project.GetProjectInputDirectory(ProjectName);
    public string OutputDirectory => _project.GetProjectOutputDirectory(ProjectName);
    public bool CanCreateInputDirectory => !string.IsNullOrWhiteSpace(InputDirectory) && !Directory.Exists(InputDirectory);
    public bool CanCreateOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory) && !Directory.Exists(OutputDirectory);
    public string ErrorText
    {
        get => _errorText;
        private set { _errorText = value; OnPropertyChanged(); }
    }

    public RelayCommand ApplyCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand CreateInputDirectoryCommand { get; }
    public RelayCommand CreateOutputDirectoryCommand { get; }
    public RelayCommand OpenInputDirectoryCommand { get; }
    public RelayCommand OpenOutputDirectoryCommand { get; }

    private void Apply()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ErrorText = Texts.InvalidName;
            return;
        }

        _project.ProjectName = ProjectName;
        _project.Author = Author;
        _project.ProjectDescription = Description;
        _project.AssistantMetadata.ShortDescription = ShortDescription;
        _project.Notice = Notice;
        _close(true);
    }

    private void CreateDirectory(string path) => RunDirectoryAction(() => Directory.CreateDirectory(path));
    private void OpenDirectory(string path) => RunDirectoryAction(() =>
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    });

    private void RunDirectoryAction(Action action)
    {
        try
        {
            action();
            ErrorText = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                   NotSupportedException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            ErrorText = string.Format(Texts.DirectoryError, ex.Message);
        }
        finally { RefreshDirectories(); }
    }

    public void RefreshDirectories()
    {
        OnPropertyChanged(nameof(InputDirectory));
        OnPropertyChanged(nameof(OutputDirectory));
        OnPropertyChanged(nameof(CanCreateInputDirectory));
        OnPropertyChanged(nameof(CanCreateOutputDirectory));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
