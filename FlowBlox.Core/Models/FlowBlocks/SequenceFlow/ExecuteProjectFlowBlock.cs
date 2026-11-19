using FlowBlox.Core.Extensions;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Attributes.FlowBlox.Core.Attributes;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Enums;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow.ExecuteProject;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Runtime.Debugging;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using static FlowBlox.Core.Util.RunnerHostResolver;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util.Projects;
using FlowBlox.Core.Util.DeepCopier;
using JsonIgnore = Newtonsoft.Json.JsonIgnoreAttribute;

namespace FlowBlox.Core.Models.FlowBlocks
{
    [FlowBloxUIGroup("ExecuteProjectFlowBlock_Groups_Project", 0)]
    [FlowBloxUIGroup("ExecuteProjectFlowBlock_Groups_Parameters", 1)]
    [Display(Name = "ExecuteProjectFlowBlock_DisplayName", Description = "ExecuteProjectFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class ExecuteProjectFlowBlock : BaseSingleResultFlowBlock
    {
        private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> ProjectNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _projectCacheLock = new();
        private string _projectCacheKey;
        private ProjectMetadata _projectCache;
        private ObservableCollection<ExecuteProjectParameterMappingEntry> _parameterMappings;

        public ExecuteProjectFlowBlock()
        {
            ParameterMappings = new();
        }

        [ConditionallyRequired]
        [ActivationCondition(ActivationMethod = nameof(IsProjectFileVisible))]
        [Display(
            Name = "ExecuteProjectFlowBlock_ProjectFile",
            Description = "ExecuteProjectFlowBlock_ProjectFile_Description",
            ResourceType = typeof(FlowBloxTexts),
            GroupName = "ExecuteProjectFlowBlock_Groups_Project",
            Order = 0)]
        [FlowBloxUI(UISelectionButtons.FLD | UISelectionButtons.FL)]
        [FlowBloxUIFileSelection("FlowBlox project (*.fbprj)|*.fbprj|All files (*.*)|*.*")]
        public string ProjectFile { get; set; }

        private bool IsProjectFileVisible() => string.IsNullOrWhiteSpace(ProjectSpaceGuid);

        [ConditionallyRequired]
        [ActivationCondition(ActivationMethod = nameof(IsProjectSpaceGuidVisible))]
        [Display(
            Name = "ExecuteProjectFlowBlock_ProjectSpaceGuid",
            Description = "ExecuteProjectFlowBlock_ProjectSpaceGuid_Description",
            ResourceType = typeof(FlowBloxTexts),
            GroupName = "ExecuteProjectFlowBlock_Groups_Project",
            Order = 1)]
        public string ProjectSpaceGuid { get; set; }

        private bool IsProjectSpaceGuidVisible() => string.IsNullOrWhiteSpace(ProjectFile);

        [JsonIgnore]
        [DeepCopierIgnore]
        [ActivationCondition(ActivationMethod = nameof(IsProjectSpaceGuidVisible))]
        [DependsOnProperty(MemberName = nameof(ProjectSpaceGuid))]
        [Display(
            Name = "ExecuteProjectFlowBlock_ProjectName",
            Description = "ExecuteProjectFlowBlock_ProjectName_Tooltip",
            ResourceType = typeof(FlowBloxTexts),
            GroupName = "ExecuteProjectFlowBlock_Groups_Project",
            Order = -1)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
        public string ProjectName => GetProjectName();

        [ActivationCondition(ActivationMethod = nameof(IsProjectSpaceGuidVisible))]
        [Display(
            Name = "ExecuteProjectFlowBlock_ProjectSpaceVersion",
            Description = "ExecuteProjectFlowBlock_ProjectSpaceVersion_Description",
            ResourceType = typeof(FlowBloxTexts),
            GroupName = "ExecuteProjectFlowBlock_Groups_Project",
            Order = 2)]
        public int? ProjectSpaceVersion { get; set; }

        [Display(
            Name = "ExecuteProjectFlowBlock_AbortOnError",
            Description = "ExecuteProjectFlowBlock_AbortOnError_Description",
            ResourceType = typeof(FlowBloxTexts),
            GroupName = "ExecuteProjectFlowBlock_Groups_Project",
            Order = 3)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool AbortOnError { get; set; } = true;

        [Display(
            Name = "ExecuteProjectFlowBlock_AbortOnWarning",
            Description = "ExecuteProjectFlowBlock_AbortOnWarning_Description",
            ResourceType = typeof(FlowBloxTexts),
            GroupName = "ExecuteProjectFlowBlock_Groups_Project",
            Order = 4)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool AbortOnWarning { get; set; } = false;

        [Display(
            Name = "ExecuteProjectFlowBlock_ParameterMappings",
            Description = "ExecuteProjectFlowBlock_ParameterMappings_Description",
            ResourceType = typeof(FlowBloxTexts),
            GroupName = "ExecuteProjectFlowBlock_Groups_Parameters",
            Order = 0)]
        [FlowBloxUI(DisplayLabel = false)]
        [FlowBloxDataGrid(IsMovable = true)]
        public ObservableCollection<ExecuteProjectParameterMappingEntry> ParameterMappings
        {
            get => _parameterMappings;
            set
            {
                if (_parameterMappings != null)
                    _parameterMappings.CollectionChanged -= ParameterMappingsChanged;
                _parameterMappings = value;
                if (_parameterMappings != null)
                    _parameterMappings.CollectionChanged += ParameterMappingsChanged;
                ParameterMappingsChanged(this, null);
            }
        }

        [JsonIgnore]
        [DeepCopierIgnore]
        [Display(Name = "ExecuteProjectFlowBlock_InputFields", Description = "ExecuteProjectFlowBlock_InputFields_Tooltip",
            ResourceType = typeof(FlowBloxTexts), GroupName = "ExecuteProjectFlowBlock_Groups_Parameters", Order = 1)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
        [FlowBloxDataGrid(ColumnMemberNames = [nameof(ProjectFieldMetadata.Name), nameof(ProjectFieldMetadata.FieldType),
            nameof(ProjectFieldMetadata.DefaultValue), nameof(ProjectFieldMetadata.IsPassword), nameof(ProjectFieldMetadata.IsRequiredAtRuntimeStartup)])]
        [FlowBloxAssociation(Operations = UIOperations.None)]
        public List<ProjectFieldMetadata> InputFields => GetInputFieldMetadata().ToList();

        private void ParameterMappingsChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            foreach (var entry in ParameterMappings ?? Enumerable.Empty<ExecuteProjectParameterMappingEntry>())
            {
                if (entry != null)
                    entry.Owner = this;
            }
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(
            string.IsNullOrWhiteSpace(ProjectSpaceGuid) ? FlowBloxIcons.project : FlowBloxIcons.project_space, 16);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(
            string.IsNullOrWhiteSpace(ProjectSpaceGuid) ? FlowBloxIcons.project : FlowBloxIcons.project_space, 32);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.ControlFlow;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(ProjectName));
            properties.Add(nameof(ProjectFile));
            properties.Add(nameof(ProjectSpaceGuid));
            properties.Add(nameof(ProjectSpaceVersion));
            return properties;
        }

        private string GetProjectName()
        {
            var projectSpaceGuid = this.Resolve(block => block.ProjectSpaceGuid);
            if (string.IsNullOrWhiteSpace(projectSpaceGuid))
                return string.Empty;

            var fallback = FlowBloxResourceUtil.GetLocalizedString(
                "ExecuteProjectFlowBlock_ProjectNameUnavailable", typeof(FlowBloxTexts));
            if (!Guid.TryParse(projectSpaceGuid, out var guid))
                return fallback;

            var baseUrl = FlowBloxOptions.GetOptionInstance()
                .GetOption(OptionsConstants.ApiProjectServiceBaseUrl).Value;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return fallback;
            var token = FlowBloxAccountManager.Instance.GetUserToken(baseUrl);
            var key = $"{baseUrl.TrimEnd('/')}|{guid:N}";
            var name = ProjectNames.GetOrAdd(key, _ => new Lazy<Task<string>>(
                () => Task.Run(() => LoadProjectNameAsync(baseUrl, guid.ToString("N"), token))));
            try
            {
                return name.Value.WaitAsync(TimeSpan.FromSeconds(GlobalConstants.RemotePropertyLoadTimeoutSeconds))
                    .GetAwaiter().GetResult() ?? fallback;
            }
            catch (TimeoutException)
            {
                return fallback;
            }
        }

        private static async Task<string> LoadProjectNameAsync(string baseUrl, string guid, string token)
        {
            try
            {
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(GlobalConstants.RemotePropertyLoadTimeoutSeconds));
                var api = new FlowBloxWebApiService(baseUrl);
                var response = await api.GetProjectAsync(
                    new FbProjectRequest { Guid = guid },
                    token,
                    timeout.Token).ConfigureAwait(false);
                var projectName = response.Success ? response.ResultObject?.Name?.Trim() : null;
                return string.IsNullOrEmpty(projectName) ? null : projectName;
            }
            catch (Exception exception)
            {
                FlowBloxLogManager.Instance.GetLogger().Warn("Could not load Project Space project name.", exception);
                return null;
            }
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return this.Invoke(runtime, data, () =>
            {

                if (string.IsNullOrWhiteSpace(ProjectSpaceGuid) && string.IsNullOrWhiteSpace(ProjectFile))
                    throw new ValidationException("Either the project file or a Project Space GUID must be specified.");

                // If a version is provided, a ProjectSpaceGuid must also be set.
                if (ProjectSpaceVersion.HasValue && string.IsNullOrWhiteSpace(ProjectSpaceGuid))
                    throw new ValidationException("ProjectSpaceGuid must be set when ProjectSpaceVersion is provided.");

                var cmd = RunnerHostResolver.Resolve();

                var workDir = CreateWorkingDirectory();
                var requestFile = Path.Combine(workDir, "request.json");
                var responseFile = Path.Combine(workDir, "response.json");

                var request = BuildRunnerRequest();

                WriteJson(requestFile, request);

                _ = StartRunnerHost(cmd, requestFile, responseFile);

                if (!File.Exists(responseFile))
                    throw new InvalidOperationException($"RunnerHost did not create a response file: {responseFile}");

                var responseJson = File.ReadAllText(responseFile);
                if (string.IsNullOrWhiteSpace(responseJson))
                    throw new InvalidOperationException("Runner response file is empty.");

                var response = JsonSerializer.Deserialize<Runner.Contracts.RunnerResponse>(
                    responseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response == null)
                    throw new InvalidOperationException("Runner response could not be deserialized.");

                var projectContext = "Child project";
                if (!string.IsNullOrWhiteSpace(response.ProjectName))
                    projectContext += $" '{response.ProjectName}'";

                if (!string.IsNullOrWhiteSpace(response.LogfilePath))
                    runtime.Report($"{projectContext}: Runtime log file is located at: {response.LogfilePath}", FlowBloxLogLevel.Info);

                foreach (var failure in response.ExecutionEnvironmentPrerequisiteFailures ?? new())
                    runtime.NotifyExecutionEnvironmentPrerequisiteFailure(failure);

                foreach (var warning in (response.Warnings ?? new())
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Distinct(StringComparer.Ordinal))
                    runtime.Report($"{projectContext}: {warning}", FlowBloxLogLevel.Warning);

                if (!response.Success)
                {
                    runtime.Report($"{projectContext}: Execution failed (ExitCode={response.ExitCode}).", FlowBloxLogLevel.Error);
                    foreach (var error in new[] { response.ErrorMessage, response.AbortMessage, response.Exception, response.AbortException }
                        .Where(message => !string.IsNullOrWhiteSpace(message))
                        .Distinct(StringComparer.Ordinal))
                        runtime.Report($"{projectContext}: {error}", FlowBloxLogLevel.Error);

                    runtime.CancelExecution(
                        RuntimeCancellationKind.Unknown,
                        string.IsNullOrWhiteSpace(response.ErrorMessage)
                            ? $"Child project execution failed (ExitCode={response.ExitCode})."
                            : response.ErrorMessage);
                    return;
                }

                GenerateResult(runtime, responseJson);
            });
        }

        private static string CreateWorkingDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "FlowBloxRunner", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private FlowBlox.Core.Runner.Contracts.RunnerRequest BuildRunnerRequest()
        {
            var userFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var optionOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in ParameterMappings ?? Enumerable.Empty<ExecuteProjectParameterMappingEntry>())
            {
                if (entry == null)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.TargetKey))
                    throw new ValidationException("TargetKey must not be empty in parameter mappings.");

                if (string.IsNullOrWhiteSpace(entry.TargetValue))
                    throw new ValidationException($"TargetValue must not be empty in parameter mappings for '{entry.TargetKey}'.");

                var value = entry.Resolve(owner => owner.TargetValue);

                if (entry.TargetType == ExecuteProjectTargetType.UserField)
                    userFields[entry.TargetKey] = value;
                else
                    optionOverrides[entry.TargetKey] = value;
            }

            return new FlowBlox.Core.Runner.Contracts.RunnerRequest
            {
                ProjectFile = this.Resolve(owner => owner.ProjectFile),

                // ProjectSpace support
                ProjectSpaceGuid = string.IsNullOrWhiteSpace(ProjectSpaceGuid)
                    ? null
                    : this.Resolve(owner => owner.ProjectSpaceGuid),

                ProjectSpaceVersion = this.Resolve(x => x.ProjectSpaceVersion),

                AutoRestart = false, // hard requirement
                AbortOnError = AbortOnError,
                AbortOnWarning = AbortOnWarning,
                UserFields = userFields,
                OptionOverrides = optionOverrides
            };
        }

        private static int StartRunnerHost(RunnerHostCommand runnerHostCommand, string requestFile, string responseFile)
        {
            var psi = new ProcessStartInfo
            {
                FileName = runnerHostCommand.FileName,
                Arguments = runnerHostCommand.BuildArguments(requestFile, responseFile),
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc.WaitForExit();
            return proc.ExitCode;
        }

        private static void WriteJson(string path, object obj)
        {
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private IReadOnlyList<ProjectFieldMetadata> GetInputFieldMetadata()
        {
            try
            {
                return GetOrCacheProject()?.InputFields ?? [];
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Could not load project input field suggestions.", ex);
                return [];
            }
        }

        public IEnumerable<string> GetInputFieldNames() => GetInputFieldMetadata()
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> GetOutputNames()
        {
            try
            {
                var project = GetOrCacheProject();
                return project?.Outputs.Keys
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [];
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("No output names could be extracted from the project.", ex);
                return [];
            }
        }

        public IEnumerable<string> GetOutputPropertyNames(string outputName)
        {
            if (string.IsNullOrWhiteSpace(outputName))
                return Enumerable.Empty<string>();

            try
            {
                var project = GetOrCacheProject();
                return project != null && project.Outputs.TryGetValue(outputName, out var fields)
                    ? fields
                    : [];
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        private ProjectMetadata GetOrCacheProject()
        {
            var cacheKey = BuildProjectCacheKey();
            if (string.IsNullOrWhiteSpace(cacheKey))
                return null;

            lock (_projectCacheLock)
            {
                if (string.Equals(_projectCacheKey, cacheKey, StringComparison.OrdinalIgnoreCase))
                    return _projectCache;
            }

            var loadedProject = LoadProjectForSuggestions();
            if (loadedProject == null)
                return null;

            var project = ProjectMetadataReader.Read(loadedProject);

            lock (_projectCacheLock)
            {
                _projectCacheKey = cacheKey;
                _projectCache = project;
            }

            return project;
        }

        private string BuildProjectCacheKey()
        {
            var projectFile = this.Resolve(owner => owner.ProjectFile);
            var projectSpaceGuid = this.Resolve(owner => owner.ProjectSpaceGuid);
            var versionPart = this.Resolve(x => x.ProjectSpaceVersion)?.ToString();

            if (!string.IsNullOrWhiteSpace(projectFile))
                return $"FILE|{projectFile}";

            if (!string.IsNullOrWhiteSpace(projectSpaceGuid))
                return $"SPACE|{projectSpaceGuid}|{versionPart}";

            return string.Empty;
        }

        private FlowBloxProject LoadProjectForSuggestions()
        {
            var projectFile = this.Resolve(owner => owner.ProjectFile);
            if (!string.IsNullOrWhiteSpace(projectFile) && File.Exists(projectFile))
                return FlowBloxProject.FromFile(projectFile);

            var projectSpaceGuid = this.Resolve(owner => owner.ProjectSpaceGuid);
            if (!string.IsNullOrWhiteSpace(projectSpaceGuid))
            {
                var baseUrl = FlowBloxOptions.GetOptionInstance()
                    .OptionCollection[OptionsConstants.ApiProjectServiceBaseUrl]
                    .Value;

                var webApi = new FlowBloxWebApiService(baseUrl);
                var token = FlowBloxAccountManager.Instance.GetUserToken(baseUrl);

                return Task.Run(async () => await FlowBloxProject
                    .FromProjectSpaceGuidAsync(
                        projectSpaceGuid,
                        this.Resolve(x => x.ProjectSpaceVersion),
                        token,
                        webApi))
                    .GetAwaiter()
                    .GetResult();
            }

            return null;
        }
    }
}
