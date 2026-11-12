using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.Controls;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.UICore.Views;

public partial class ProjectExecutionWindow : MetroWindow
{
    private readonly ProjectShortcut _shortcut;
    private readonly ProjectExecutions _executions;
    private readonly FlowBloxProject? _project;
    private readonly List<FieldElement> _fields = new();
    private readonly ObservableCollection<FlowBloxTaskOptionOverrideViewModel> _options = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private ProjectExecutionEntry? _entry;
    private string _inputDirectory = string.Empty;
    private string _outputDirectory = string.Empty;

    public ProjectExecutionWindow(ProjectShortcut shortcut, FlowBloxProject project, ProjectExecutions? executions = null)
    {
        InitializeComponent();
        _shortcut = shortcut;
        _executions = executions ?? ProjectExecutions.Instance;
        _project = project;
        ProjectTitle.Text = project.ProjectName;
        ProjectDescription.Text = project.ProjectDescription;
        OptionsGrid.ItemsSource = _options;
        BuildFields();
        InitializeMonitoring();
        UpdateExecuteAvailability();
    }

    public ProjectExecutionWindow(ProjectExecutionEntry entry, ProjectExecutions? executions = null)
    {
        InitializeComponent();
        _shortcut = entry.Project;
        _executions = executions ?? ProjectExecutions.Instance;
        _entry = entry;
        ProjectTitle.Text = entry.Project.Name;
        ProjectDescription.Text = Text(nameof(Texts.Readonly));
        foreach (var pair in entry.Request.UserFields.OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase))
            AddField(pair.Key, new TextBox { Text = pair.Value, IsReadOnly = true });
        foreach (var pair in entry.Request.OptionOverrides)
        {
            var option = new FlowBloxTaskOptionOverrideViewModel { OptionKey = pair.Key, StringValue = pair.Value };
            option.SetAvailableOptionKeys(new[] { pair.Key });
            _options.Add(option);
        }
        OptionsGrid.ItemsSource = _options;
        Configuration.IsEnabled = false;
        ExecuteButton.Visibility = Visibility.Collapsed;
        InitializeMonitoring();
    }

    private void InitializeMonitoring()
    {
        InputDirectoryButton.Command = new RelayCommand(async () => await OpenDirectoryAsync(_inputDirectory));
        OutputDirectoryButton.Command = new RelayCommand(async () => await OpenDirectoryAsync(_outputDirectory));
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) =>
        {
            if (_entry?.IsRunning == true)
                _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            foreach (var field in _fields)
                field.PropertyChanged -= InputField_PropertyChanged;
        };
        RefreshState();
    }

    private void BuildFields()
    {
        var resolver = new PropertyControlResolver(this);
        var property = typeof(FieldElement).GetProperty(nameof(FieldElement.StringValue))!;
        foreach (var field in (_project!.UserFields ?? Enumerable.Empty<FieldElement>())
                     .Where(field => field.IsUserInputField())
                     .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            _fields.Add(field);
            var bindingContext = new PropertyControlBindingContext(field);
            var model = resolver.Resolve(property, field, false, null, bindingContext);
            AddField(field.Name + (field.IsRequiredAtRuntimeStartup ? " *" : string.Empty), model.Control);
            field.PropertyChanged += InputField_PropertyChanged;
        }
        if (_fields.Count == 0)
            FieldsPanel.Children.Add(new TextBlock { Text = Text(nameof(Texts.NoParameters)), Margin = new Thickness(4) });
    }

    private void InputField_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(FieldElement.StringValue))
            UpdateExecuteAvailability();
    }

    private void UpdateExecuteAvailability()
    {
        ExecuteButton.IsEnabled = _entry == null && _fields
            .Where(field => field.IsRequiredAtRuntimeStartup)
            .All(field => !string.IsNullOrWhiteSpace(field.StringValue));
    }

    private void AddField(string name, FrameworkElement editor)
    {
        var label = new TextBlock { Text = name, Style = (Style)FindResource("PropertyView.LabelTextStyle") };
        FieldsPanel.Children.Add(new Border
        {
            Child = label,
            Style = (Style)FindResource("PropertyView.LabelBorderStyle"),
            Margin = new Thickness(0, 4, 0, 3)
        });
        editor.Margin = new Thickness(0, 0, 0, 8);
        FieldsPanel.Children.Add(editor);
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (!ExecuteButton.IsEnabled || _entry != null)
            return;
        try
        {
            OptionsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            OptionsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var values = _fields.ToDictionary(x => x.Name, x => x.StringValue ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            var missing = _fields.FirstOrDefault(x => x.IsRequiredAtRuntimeStartup && string.IsNullOrWhiteSpace(values[x.Name]));
            if (missing != null)
                throw new InvalidOperationException(string.Format(Text(nameof(Texts.Required)), missing.Name));
            var overrides = _options.Where(x => !string.IsNullOrWhiteSpace(x.OptionKey)).ToList();
            if (overrides.GroupBy(x => x.OptionKey, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                throw new InvalidOperationException(Text(nameof(Texts.DuplicateOption)));

            var request = new RunnerRequest
            {
                UserFields = values,
                OptionOverrides = overrides.ToDictionary(x => x.OptionKey, x => x.StringValue ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            };
            RefreshDirectories();
            _entry = _executions.Start(_shortcut, _project, request, _inputDirectory, _outputDirectory);
            Close();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private void AddOption_Click(object sender, RoutedEventArgs e)
    {
        var option = new FlowBloxTaskOptionOverrideViewModel();
        option.SetAvailableOptionKeys(FlowBloxOptions.GetOptionInstance().OptionCollection.Keys);
        option.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(option.OptionKey) && !string.IsNullOrWhiteSpace(option.OptionKey))
                OptionsGrid.ToolTip = FlowBloxOptions.GetOptionInstance().GetOption(option.OptionKey)?.Description
                    ?? Text(nameof(Texts.OptionTooltip));
            RefreshDirectories();
        };
        _options.Add(option);
        OptionsGrid.SelectedItem = option;
    }

    private void RemoveOption_Click(object sender, RoutedEventArgs e)
    {
        foreach (var option in OptionsGrid.SelectedItems.OfType<FlowBloxTaskOptionOverrideViewModel>().ToList())
            _options.Remove(option);
        RefreshDirectories();
    }

    private void OptionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveOptionButton.IsEnabled = OptionsGrid.SelectedItems.Count > 0;

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        try
        {
            RefreshState();
        }
        catch (Exception exception)
        {
            _timer.Stop();
            await ShowErrorAsync(exception);
        }
    }

    private void RefreshState()
    {
        _executions.Refresh();
        if (_entry != null)
        {
            _entry = _executions.GetAll().FirstOrDefault(x => x.Id == _entry.Id) ?? _entry;
            StatusText.Text = string.Join(Environment.NewLine,
                _entry.IsRunning ? Text(nameof(Texts.Running)) : string.Empty,
                string.Format(Text(nameof(Texts.ExecutionStarted)), _entry.StartedUtc.ToLocalTime())).Trim();
            RunningIndicator.Visibility = _entry.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            CompletionBox.Visibility = _entry.IsRunning ? Visibility.Collapsed : Visibility.Visible;
            if (!_entry.IsRunning)
            {
                _timer.Stop();
                RefreshCompletion();
            }
        }
        RefreshDirectories();
    }

    private void RefreshCompletion()
    {
        var succeeded = _entry!.Response?.Success == true;
        var color = succeeded ? Color.FromRgb(0x42, 0x97, 0x66) : Color.FromRgb(0xD7, 0x77, 0x6C);
        CompletionBox.Background = new SolidColorBrush(color) { Opacity = 0.12 };
        CompletionBox.BorderBrush = new SolidColorBrush(color) { Opacity = 0.6 };
        CompletionTitle.Text = Text(succeeded ? nameof(Texts.Succeeded) : nameof(Texts.Failed));
        CompletionMessage.Text = new[]
        {
            _entry.Response?.ErrorMessage,
            _entry.Response?.CancellationReason,
            _entry.Response?.AbortMessage,
            _entry.LaunchError
        }.FirstOrDefault(message => !string.IsNullOrWhiteSpace(message)) ?? string.Empty;
        CompletionMessage.Visibility = string.IsNullOrEmpty(CompletionMessage.Text) ? Visibility.Collapsed : Visibility.Visible;
        var hasLogfile = File.Exists(_entry.Response?.LogfilePath);
        LogLinkContainer.Visibility = hasLogfile ? Visibility.Visible : Visibility.Collapsed;
        LogLink.ToolTip = _entry.Response?.LogfilePath;
        LogUnavailableText.Visibility = hasLogfile ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshDirectories()
    {
        _inputDirectory = _entry?.InputDirectory ?? _project?.ProjectInputDirectory ?? string.Empty;
        _outputDirectory = _entry?.OutputDirectory ?? _project?.ProjectOutputDirectory ?? string.Empty;
        if (_entry == null && _project != null)
        {
            var inputOverride = _options.FirstOrDefault(x => x.OptionKey == OptionsConstants.PathsInputDir);
            var outputOverride = _options.FirstOrDefault(x => x.OptionKey == OptionsConstants.PathsOutputDir);
            if (inputOverride != null)
                _inputDirectory = Path.Combine(Environment.ExpandEnvironmentVariables(inputOverride.StringValue ?? string.Empty),
                    IOUtil.GetValidFileName(_project.ProjectName).Trim());
            if (outputOverride != null)
                _outputDirectory = Path.Combine(Environment.ExpandEnvironmentVariables(outputOverride.StringValue ?? string.Empty),
                    IOUtil.GetValidFileName(_project.ProjectName).Trim());
        }
        InputDirectoryButton.IsEnabled = !string.IsNullOrWhiteSpace(_inputDirectory);
        OutputDirectoryButton.IsEnabled = !string.IsNullOrWhiteSpace(_outputDirectory);
        InputPath.Text = _inputDirectory;
        OutputPath.Text = _outputDirectory;
    }

    private async Task OpenDirectoryAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_entry?.Response?.LogfilePath))
                FlowBloxEditingHelper.OpenUsingEditor(_entry.Response.LogfilePath);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private async Task ShowErrorAsync(Exception exception)
    {
        FlowBloxLogManager.Instance.GetLogger().Error("Project launcher operation failed.", exception);
        await MessageBoxHelper.ShowMessageBoxAsync(this, MessageBoxType.Error,
            string.Format(Text(nameof(Texts.Error)), exception.Message));
    }

    private static string Text(string key) => FlowBloxResourceUtil.GetLocalizedString(key, typeof(Texts));
}
