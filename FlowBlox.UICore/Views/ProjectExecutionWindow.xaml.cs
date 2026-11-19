using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Projects;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using Newtonsoft.Json.Linq;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.UICore.Views;

public partial class ProjectExecutionWindow : MetroWindow
{
    private readonly ProjectShortcut _shortcut;
    private readonly ProjectExecutions _executions;
    private readonly ProjectInputConfigurations _inputConfigurations = ProjectInputConfigurations.Instance;
    private readonly FlowBloxProject? _project;
    private readonly List<FieldElement> _fields = new();
    private readonly IReadOnlyList<ProjectOutputDirectoryChoice> _outputDirectories = Array.Empty<ProjectOutputDirectoryChoice>();
    private readonly Dictionary<string, string> _initialValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<FlowBloxTaskOptionOverrideViewModel> _options = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private ProjectExecutionEntry? _entry;
    private string _inputDirectory = string.Empty;
    private string _outputDirectory = string.Empty;
    private bool _completionShown;
    private bool _refreshingConfigurations;

    public ProjectExecutionWindow(
        ProjectShortcut shortcut,
        FlowBloxProject project,
        ProjectExecutions? executions = null,
        IEnumerable<string>? projectNames = null,
        ProjectInputConfigurations? inputConfigurations = null)
    {
        InitializeComponent();
        _shortcut = shortcut;
        _executions = executions ?? ProjectExecutions.Instance;
        _inputConfigurations = inputConfigurations ?? ProjectInputConfigurations.Instance;
        _project = project;
        _outputDirectories = ProjectOutputDirectoryChoice.Create(
            (projectNames ?? Enumerable.Empty<string>()).Append(project.ProjectName));
        ProjectTitle.Text = project.ProjectName;
        ProjectDescription.Text = project.ProjectDescription;
        TextBlockLinkHelper.SetFormattedText(ProjectNoticeText, project.Notice);
        ProjectNoticeExpander.Visibility = string.IsNullOrWhiteSpace(project.Notice) ? Visibility.Collapsed : Visibility.Visible;
        OptionsGrid.ItemsSource = _options;
        BuildFields();
        InitializeInputConfigurations();
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
        ProjectDescription.Text = ReadProjectDescription(entry);
        foreach (var pair in entry.Request.OptionOverrides)
        {
            var option = new FlowBloxTaskOptionOverrideViewModel { OptionKey = pair.Key, StringValue = pair.Value };
            option.SetAvailableOptionKeys(new[] { pair.Key });
            _options.Add(option);
        }
        OptionsGrid.ItemsSource = _options;
        SetConfigurationReadOnly();
        OptionOverridesExpander.IsExpanded = _options.Count > 0;
        ExecuteButton.Visibility = Visibility.Collapsed;
        InitializeMonitoring();
    }

    private void InitializeMonitoring()
    {
        ProjectDescriptionBox.Visibility = string.IsNullOrWhiteSpace(ProjectDescription.Text) ? Visibility.Collapsed : Visibility.Visible;
        NoParametersHint.Visibility = FieldsPanel.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        var inputMetadata = ProjectMetadataReader.Read(_project!).InputFields
            .ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var field in (_project!.UserFields ?? Enumerable.Empty<FieldElement>())
                     .Where(field => field.IsUserInputField())
                     .OrderBy(x => x.Index))
        {
            field.StringValue = string.Empty;

            _fields.Add(field);
            _initialValues[field.Name] = inputMetadata[field.Name].DefaultValue ?? string.Empty;
            var bindingContext = new PropertyControlBindingContext(field);
            var model = resolver.Resolve(property, field, false, null, bindingContext);
            var editor = AddOutputDirectoryButton(field, model.Control);
            AddField(field.Name + (field.IsRequiredAtRuntimeStartup ? " *" : string.Empty), editor);
            field.PropertyChanged += InputField_PropertyChanged;
        }
    }

    private FrameworkElement AddOutputDirectoryButton(FieldElement field, FrameworkElement editor)
    {
        if (field.LoVType == LoVType.PossibleValues)
            return editor;

        var button = new Button
        {
            ToolTip = FlowBloxResourceUtil.GetLocalizedString("ProjectOutputDirectoryChoice_Select", typeof(Core.FlowBloxTexts)),
            Content = new PackIconMaterial
            {
                Kind = PackIconMaterialKind.FolderUploadOutline,
                Width = 10,
                Height = 10
            },
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        ((PackIconMaterial)button.Content).SetResourceReference(StyleProperty, "PropertyView.FieldSelectionIconStyle");
        button.Click += (_, _) => SelectOutputDirectory(field);
        TextBoxWithOptionalButtonsCreator.TryAddActionButton(editor, button);
        return editor;
    }

    private void SelectOutputDirectory(FieldElement field)
    {
        var dialog = new MultiValueSelectionDialog(
            FlowBloxResourceUtil.GetLocalizedString("ProjectOutputDirectoryChoice_Select", typeof(Core.FlowBloxTexts)),
            FlowBloxResourceUtil.GetLocalizedString("ProjectOutputDirectoryChoice_Description", typeof(Core.FlowBloxTexts)),
            new GenericSelectionHandler<ProjectOutputDirectoryChoice>(_outputDirectories, choice => choice.DisplayName))
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (dialog.ShowDialog() != true || dialog.SelectedItem?.Value is not ProjectOutputDirectoryChoice choice)
            return;

        field.StringValue = choice.Value;
        FlowBloxComponentHelper.RaisePropertyChanged(field, nameof(FieldElement.StringValue));
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
        var label = new TextBlock { Text = name, Style = (Style)FindResource("Execution.ParameterLabel") };
        FieldsPanel.Children.Add(new Border
        {
            Child = label,
            Style = (Style)FindResource("Execution.ParameterBorder"),
            Margin = new Thickness(0, 0, 0, 3)
        });
        editor.Margin = new Thickness(0, 0, 0, 6);
        FieldsPanel.Children.Add(editor);
    }

    private void InitializeInputConfigurations(Guid? selectedId = null)
    {
        var configurations = _inputConfigurations.GetForProject(_shortcut)
            .Select(configuration => new ProjectInputConfigurationItem(configuration, _fields))
            .ToList();
        _refreshingConfigurations = true;
        try
        {
            ConfigurationList.ItemsSource = configurations;
            InputConfigurationsExpander.Visibility = configurations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearInputsButton.Visibility = _fields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ConfigurationList.SelectedItem = configurations.FirstOrDefault(item => item.Configuration.Id == selectedId);
        }
        finally
        {
            _refreshingConfigurations = false;
        }
    }

    private void ConfigurationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingConfigurations || _project == null || _entry != null || ConfigurationList.SelectedItem is not ProjectInputConfigurationItem item)
            return;

        ResetConfiguration();
        var values = new Dictionary<string, string>(item.Configuration.Request.UserFields, StringComparer.OrdinalIgnoreCase);
        foreach (var field in _fields)
        {
            if (values.TryGetValue(field.Name, out var value))
            {
                field.StringValue = value;
                FlowBloxComponentHelper.RaisePropertyChanged(field, nameof(FieldElement.StringValue));
            }
        }
        foreach (var pair in item.Configuration.Request.OptionOverrides)
            AddOption(pair.Key, pair.Value);

        RefreshDirectories();
        UpdateExecuteAvailability();
    }

    private void ClearInputs_Click(object sender, RoutedEventArgs e)
    {
        ConfigurationList.SelectedItem = null;
        ResetConfiguration();
    }

    private async void SaveConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || _entry != null)
            return;

        try
        {
            OptionsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            OptionsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var name = EditConfigurationName(string.Empty);
            if (name == null)
                return;

            var configuration = new ProjectInputConfiguration
            {
                Name = name,
                Project = _shortcut,
                Request = new RunnerRequest
                {
                    UserFields = _fields.ToDictionary(field => field.Name, field => field.StringValue ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                    PasswordFieldNames = _fields.Where(field => field.IsPassword).Select(field => field.Name).ToList(),
                    OptionOverrides = _options.Where(option => !string.IsNullOrWhiteSpace(option.OptionKey))
                        .ToDictionary(option => option.OptionKey, option => option.StringValue ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                }
            };
            _inputConfigurations.Save(configuration);
            InitializeInputConfigurations(configuration.Id);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private async void RenameConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (_entry != null || sender is not MenuItem menu || menu.Tag is not ProjectInputConfigurationItem item)
            return;

        try
        {
            var name = EditConfigurationName(item.Name);
            if (name == null)
                return;

            var selectedId = (ConfigurationList.SelectedItem as ProjectInputConfigurationItem)?.Configuration.Id;
            _inputConfigurations.Rename(_shortcut, item.Configuration.Id, name);
            InitializeInputConfigurations(selectedId);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private string? EditConfigurationName(string value)
    {
        var dialog = new EditValueWindow(value, false, false)
        {
            Owner = this,
            Title = Text(nameof(Texts.ConfigurationName)),
            Description = Text(nameof(Texts.ConfigurationNameDescription))
        };
        return dialog.ShowDialog() == true ? dialog.GetValue().Trim() : null;
    }

    private void ResetConfiguration()
    {
        OptionsGrid.CancelEdit(DataGridEditingUnit.Cell);
        OptionsGrid.CancelEdit(DataGridEditingUnit.Row);
        _options.Clear();
        foreach (var field in _fields)
        {
            field.StringValue = _initialValues[field.Name];
            FlowBloxComponentHelper.RaisePropertyChanged(field, nameof(FieldElement.StringValue));
        }
        RefreshDirectories();
        UpdateExecuteAvailability();
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
            SetConfigurationReadOnly();
            ExecuteButton.Visibility = Visibility.Collapsed;
            RefreshState();
            if (_entry.IsRunning)
                _timer.Start();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private void AddOption_Click(object sender, RoutedEventArgs e)
    {
        AddOption(string.Empty, string.Empty);
        OptionsGrid.SelectedItem = _options.Last();
    }

    private void SetConfigurationReadOnly()
    {
        FieldsPanel.Children.Clear();
        foreach (var pair in _entry!.Request.UserFields)
        {
            var isPassword = _entry.Request.PasswordFieldNames == null
                || _entry.Request.PasswordFieldNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase);
            FrameworkElement editor = isPassword
                ? new PasswordValueEditor { Text = pair.Value ?? string.Empty, IsReadOnly = true }
                : new TextBox { Text = pair.Value, IsReadOnly = true };
            AddField(pair.Key, editor);
        }
        OptionsGrid.IsReadOnly = true;
        AddOptionButton.IsEnabled = false;
        RemoveOptionButton.IsEnabled = false;
        ClearInputsButton.IsEnabled = false;
        InputConfigurationsExpander.IsEnabled = false;
        SaveConfigurationButton.IsEnabled = false;
    }

    private void AddOption(string key, string value)
    {
        var option = new FlowBloxTaskOptionOverrideViewModel { OptionKey = key, StringValue = value };
        option.SetAvailableOptionKeys(FlowBloxOptions.GetOptionInstance().OptionCollection.Keys);
        option.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(option.OptionKey) && !string.IsNullOrWhiteSpace(option.OptionKey))
                OptionsGrid.ToolTip = FlowBloxOptions.GetOptionInstance().GetOption(option.OptionKey)?.Description
                    ?? Text(nameof(Texts.OptionTooltip));
            RefreshDirectories();
        };
        _options.Add(option);
        OptionOverridesExpander.IsExpanded = true;
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
            NotStartedHint.Visibility = Visibility.Collapsed;
            _entry = _executions.GetAll().FirstOrDefault(x => x.Id == _entry.Id) ?? _entry;
            StartedBox.Visibility = Visibility.Visible;
            StatusText.Text = string.Format(Text(nameof(Texts.ExecutionStarted)), _entry.StartedUtc.ToLocalTime());
            RunnerProcessText.Text = string.Format(Text(nameof(Texts.ProcessId)), _entry.ProcessId);
            RunnerProcessText.Visibility = _entry.ProcessId.HasValue ? Visibility.Visible : Visibility.Collapsed;
            TerminateButton.Visibility = _executions.CanTerminate(_entry) ? Visibility.Visible : Visibility.Collapsed;
            RunningIndicator.Visibility = _entry.IsRunning ? Visibility.Visible : Visibility.Collapsed;
            CompletionBox.Visibility = _entry.IsRunning ? Visibility.Collapsed : Visibility.Visible;
            if (!_entry.IsRunning)
            {
                _timer.Stop();
                RefreshCompletion();
                if (!_completionShown)
                {
                    OutputsView.SetOutputs(ProjectExecutions.ReadOutputs(_entry));
                    if (_entry.Response?.Success == true)
                        ExecutionTabs.SelectedItem = ResultsTab;
                    _completionShown = true;
                }
            }
        }
        RefreshDirectories();
    }

    private void RefreshCompletion()
    {
        var succeeded = _entry!.Response?.Success == true;
        CompletionBox.SetResourceReference(StyleProperty, succeeded
            ? "FlowBlox.NotificationBox.Success"
            : "FlowBlox.NotificationBox.Error");
        CompletionTitle.Text = Text(succeeded ? nameof(Texts.Succeeded) : nameof(Texts.Failed));
        var messages = new[]
        {
            _entry.Response?.ErrorMessage,
            _entry.Response?.CancellationReason,
            _entry.Response?.AbortMessage,
            _entry.Response?.Exception,
            _entry.Response?.AbortException,
            _entry.LaunchError
        }.Where(message => !string.IsNullOrWhiteSpace(message)).Distinct(StringComparer.Ordinal);
        CompletionMessage.Text = string.Join(Environment.NewLine, messages);
        CompletionMessage.Visibility = string.IsNullOrEmpty(CompletionMessage.Text) ? Visibility.Collapsed : Visibility.Visible;
        var hasLogfile = File.Exists(_entry.Response?.LogfilePath);
        LogLinkContainer.Visibility = hasLogfile ? Visibility.Visible : Visibility.Collapsed;
        LogLink.ToolTip = _entry.Response?.LogfilePath;
        LogUnavailableText.Visibility = hasLogfile ? Visibility.Collapsed : Visibility.Visible;
        WarningMessages.Text = string.Join(Environment.NewLine, (_entry.Response?.Warnings ?? new())
            .Where(message => !string.IsNullOrWhiteSpace(message)).Distinct(StringComparer.Ordinal));
        WarningsExpander.Visibility = string.IsNullOrEmpty(WarningMessages.Text) ? Visibility.Collapsed : Visibility.Visible;
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
        ResetArtifactsButton.IsEnabled = !string.IsNullOrWhiteSpace(_outputDirectory)
            && !_executions.IsArtifactDirectoryInUse(_outputDirectory);
    }

    private async void OpenArtifacts_Click(object sender, RoutedEventArgs e) => await OpenDirectoryAsync(_outputDirectory);

    private async void ResetArtifacts_Click(object sender, RoutedEventArgs e)
    {
        if (await MessageBoxHelper.ShowQuestionAsync(this,
            string.Format(Text(nameof(Texts.ConfirmResetArtifacts)), _outputDirectory)) != true)
            return;

        try
        {
            _executions.ClearArtifacts(_outputDirectory);
            RefreshState();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
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

    private async void Terminate_Click(object sender, RoutedEventArgs e)
    {
        if (_entry == null)
            return;

        TerminateButton.IsEnabled = false;
        try
        {
            await Task.Run(() => _executions.Terminate(_entry.Id));
            RefreshState();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
        finally
        {
            TerminateButton.IsEnabled = true;
        }
    }

    private async void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_entry?.Response?.LogfilePath))
                FlowBloxEditingHelper.OpenFileWithSelection(_entry.Response.LogfilePath, this);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private Task ShowErrorAsync(Exception exception)
    {
        FlowBloxLogManager.Instance.GetLogger().Error("Project launcher operation failed.", exception);
        FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>().ShowMessageBox(
            string.Format(Text(nameof(Texts.Error)), exception.Message), Text(nameof(Texts.Title)), FlowBloxMessageBoxTypes.Error);
        return Task.CompletedTask;
    }

    private static string ReadProjectDescription(ProjectExecutionEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ProjectDescription))
            return entry.ProjectDescription;

        var file = File.Exists(entry.Request.ProjectFile) ? entry.Request.ProjectFile : entry.Project.ProjectFile;
        if (!File.Exists(file))
            return string.Empty;

        try
        {
            return JObject.Parse(File.ReadAllText(file)).Value<string>(nameof(FlowBloxProject.ProjectDescription)) ?? string.Empty;
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Warn("Failed to read the execution's project description.", exception);
            return string.Empty;
        }
    }

    private static string Text(string key) => FlowBloxResourceUtil.GetLocalizedString(key, typeof(Texts));
}
