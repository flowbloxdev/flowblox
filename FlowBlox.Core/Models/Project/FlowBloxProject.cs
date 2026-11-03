using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Factories;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Provider.Placeholders.InputFile;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Json;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Runtime.Loader;
using System.Text;
using static FlowBlox.Core.ExternalServices.FlowBloxWebApi.FlowBloxWebApiService;

namespace FlowBlox.Core.Models.Project
{
    [Serializable]
    public class FlowBloxProject
    {
        [field: NonSerialized]
        public event EventHandler<ProjectGuidChangedEventArgs> ProjectGuidChanged;

        public Guid ProjectGuid { get; set; }
        public string ProjectName { get; set; }
        public string Author { get; set; }
        public string ProjectDescription { get; set; }
        public AssistantMetadata AssistantMetadata { get; set; } = new();
        public string Notice { get; set; }
        public int GridSizeX { get; set; }
        public int GridSizeY { get; set; }
        public bool AutoIncreaseGridSize { get; set; }

        [JsonIgnore]
        public FlowBloxRegistry FlowBloxRegistry { get; }

        public List<BaseFlowBlock> FlowBlocks { get; set; }

        public List<IManagedObject> ManagedObjects { get; set; }

        public List<FieldElement> UserFields { get; set; }

        public List<IProjectDependendData> ProjectDependendDataObjects { get; set; }

        [JsonIgnore]
        private Dictionary<string, AssemblyLoadContext> _loadContexts = new Dictionary<string, AssemblyLoadContext>(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
        public List<FlowBloxProjectExtension> Extensions { get; set; }

        /// <summary>
        /// Project space identifier. Stored locally in *.fblocaldata (not in the project file).
        /// </summary>
        [JsonIgnore]
        public string ProjectSpaceGuid { get; set; }

        /// <summary>
        /// Project space version. Stored locally in *.fblocaldata (not in the project file).
        /// </summary>
        [JsonIgnore]
        public int? ProjectSpaceVersion { get; set; }

        /// <summary>
        /// Project space endpoint URI. Stored locally in *.fblocaldata (not in the project file).
        /// </summary>
        [JsonIgnore]
        public string ProjectSpaceEndpointUri { get; set; }

        private static readonly ILogger _logger = FlowBloxLogManager.Instance.GetLogger();

        /// <summary>
        /// Gets the input directory derived from options + sanitized project name.
        /// </summary>
        [JsonIgnore]
        public string ProjectInputDirectory => GetProjectInputDirectory(ProjectName);

        /// <summary>
        /// Gets the output directory derived from options + sanitized project name.
        /// </summary>
        [JsonIgnore]
        public string ProjectOutputDirectory => GetProjectOutputDirectory(ProjectName);

        /// <summary>
        /// Gets the absolute path of the local project file.
        /// The value is empty for projects that have not been saved locally, such as newly created or Project Space projects.
        /// </summary>
        [JsonIgnore]
        public string ProjectFilePath { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the directory containing the local project file.
        /// </summary>
        [JsonIgnore]
        public string ProjectDirectory => string.IsNullOrWhiteSpace(ProjectFilePath)
            ? string.Empty
            : Path.GetDirectoryName(ProjectFilePath) ?? string.Empty;

        /// <summary>
        /// Builds the input directory path for the given project name.
        /// </summary>
        public string GetProjectInputDirectory(string projectName)
        {
            var baseDir = FlowBloxOptions.GetOptionInstance().GetOption("Paths.InputDir")?.Value;
            return BuildProjectDir(baseDir, projectName);
        }

        /// <summary>
        /// Builds the output directory path for the given project name.
        /// </summary>
        public string GetProjectOutputDirectory(string projectName)
        {
            var baseDir = FlowBloxOptions.GetOptionInstance().GetOption("Paths.OutputDir")?.Value;
            return BuildProjectDir(baseDir, projectName);
        }

        private static string BuildProjectDir(string baseDir, string projectName)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                return string.Empty;

            var safeName = IOUtil.GetValidFileName(projectName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(safeName))
                return string.Empty;

            return Path.Combine(baseDir, safeName);
        }

        [JsonProperty("InputFiles")]
        public List<FlowBloxInputFile> InputFiles { get; set; }

        [JsonProperty("InputTemplates")]
        private List<FlowBloxInputFile> LegacyInputFiles
        {
            set
            {
                if (value != null)
                    InputFiles = value;
            }
        }

        public FlowBloxProject()
        {
            ProjectGuid = Guid.NewGuid();
            GridSizeX = GlobalConstants.GridSizeX;
            GridSizeY = GlobalConstants.GridSizeY;
            AutoIncreaseGridSize = true;
            FlowBloxRegistry = new FlowBloxRegistry();
            Extensions = new List<FlowBloxProjectExtension>();
            ProjectDependendDataObjects = new List<IProjectDependendData>();
            InputFiles = new List<FlowBloxInputFile>();
            _logger.Info("FlowBloxProject instance created.");
        }

        /// <summary>
        /// Returns a static list of project properties that can be used as placeholders via $Project::{Key}.
        /// </summary>
        public IReadOnlyList<FlowBloxProjectPropertyElement> GetProjectPropertyElements()
        {
            return new List<FlowBloxProjectPropertyElement>
            {
                new FlowBloxProjectPropertyElement
                {
                    Key = "Name",
                    DisplayName = "Name",
                    Description = "The name of the project.",
                    Value = ProjectName ?? string.Empty
                },
                new FlowBloxProjectPropertyElement
                {
                    Key = "InputDirectory",
                    DisplayName = "Input Directory",
                    Description = "Resolved input directory for this project.",
                    Value = ProjectInputDirectory ?? string.Empty
                },
                new FlowBloxProjectPropertyElement
                {
                    Key = "OutputDirectory",
                    DisplayName = "Output Directory",
                    Description = "Resolved output directory for this project.",
                    Value = ProjectOutputDirectory ?? string.Empty
                },
                new FlowBloxProjectPropertyElement
                {
                    Key = "Directory",
                    DisplayName = "Project Directory",
                    Description = "Directory containing the local project file. Empty if the project has not been saved locally.",
                    Value = ProjectDirectory
                },
                new FlowBloxProjectPropertyElement
                {
                    Key = "FilePath",
                    DisplayName = "Project File Path",
                    Description = "Absolute path of the local project file. Empty if the project has not been saved locally.",
                    Value = ProjectFilePath
                },
                new FlowBloxProjectPropertyElement
                {
                    Key = "Author",
                    DisplayName = "Author",
                    Description = "Author of the project.",
                    Value = Author ?? string.Empty
                },
                new FlowBloxProjectPropertyElement
                {
                    Key = "Description",
                    DisplayName = "Description",
                    Description = "Description of the project.",
                    Value = ProjectDescription ?? string.Empty
                }
            };
        }

        /// <summary>
        /// Returns available input file placeholders for command editing.
        /// </summary>
        public IReadOnlyList<FlowBloxInputFilePlaceholderElement> GetInputFilePlaceholderElements(FlowBloxInputFile inputFile = null)
        {
            return FlowBloxInputFilePlaceholderProvider.GetElements(ProjectInputDirectory, inputFile);
        }

        /// <summary>
        /// Legacy placeholder aliases (AliasKey -> CanonicalKey) for backward compatibility.
        /// Example: $Project::ProjectOutputDirectory -> $Project::OutputDirectory
        /// </summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, string> ProjectPropertyPlaceholderAliases
        {
            get
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "ProjectName", "Name" },
                    { "ProjectInputDirectory", "InputDirectory" },
                    { "ProjectOutputDirectory", "OutputDirectory" },
                    { "ProjectAuthor", "Author" },
                    { "ProjectDescription", "Description" }
                };
            }
        }

        private static Dictionary<string, AssemblyLoadContext> LoadExtensions(IEnumerable<FlowBloxProjectExtension> extensions)
        {
            var loadContexts = new Dictionary<string, AssemblyLoadContext>(StringComparer.OrdinalIgnoreCase);
            foreach (var extension in extensions)
            {
                LoadExtension(extension, loadContexts);
            }
            return loadContexts;
        }

        public event EventHandler ExtensionsReloaded;

        public FlowBloxProjectReloadResult ReloadExtensions()
        {
            var currentExtensionDirectories = new HashSet<string>(
                Extensions.Select(e => e.LocalExtensionDirectory),
                StringComparer.OrdinalIgnoreCase);

            var directoriesToUnload = _loadContexts.Keys
                .Where(key => !currentExtensionDirectories.Contains(key))
                .ToList();

            return ReloadExtensions(directoriesToUnload, Extensions);
        }

        public FlowBloxProjectReloadResult ReloadExtensions(IEnumerable<string> directoriesToUnload)
        {
            List<FlowBloxProjectExtension> extensionsToLoad = new List<FlowBloxProjectExtension>();
            return ReloadExtensions(directoriesToUnload, extensionsToLoad);
        }

        public FlowBloxProjectReloadResult ReloadExtensions(IEnumerable<string> directoriesToUnload, List<FlowBloxProjectExtension> extensionsToLoad)
        {
            var reloadResult = new FlowBloxProjectReloadResult();
            _logger.Info("Reloading extensions...");

            var weakReferences = new Dictionary<WeakReference, List<string>>();

            foreach (var directory in directoriesToUnload)
            {
                if (_loadContexts.TryGetValue(directory, out var loadContext))
                {
                    var remainingAssemblies = loadContext.Assemblies
                        .Select(a => a.FullName)
                        .ToList();

                    var weakReference = new WeakReference(loadContext);
                    weakReferences[weakReference] = remainingAssemblies!;
                    UnloadExtension(directory, loadContext);
                    loadContext = null;
                }
            }

            foreach (var extension in extensionsToLoad)
            {
                var loadContext = LoadExtension(extension, _loadContexts);

                if (loadContext == null || !loadContext.Assemblies.Any())
                {
                    reloadResult.Success = false;
                    reloadResult.UnloadableExtensions.Add($"{extension.Name}/{extension.Version}");
                    _logger.Warn($"Failed to load extension: {extension.Name} Version: {extension.Version}");
                }
            }

            foreach (var kvp in weakReferences)
            {
                for (int i = 0; kvp.Key.IsAlive && (i < 10); i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                if (kvp.Key.IsAlive)
                {
                    reloadResult.Success = false;
                    reloadResult.RemainingAssemblies.AddRange(kvp.Value);
                }
            }

            if (reloadResult.Success)
                _logger.Info("Extensions reloaded successfully.");
            else
            {
                if (reloadResult.RemainingAssemblies.Any())
                    _logger.Warn($"Extensions could not be reloaded successfully. Remaining assemblies: {string.Join(", ", reloadResult.RemainingAssemblies)}.");

                if (reloadResult.UnloadableExtensions.Any())
                    _logger.Warn($"Failed to load the following extensions: {string.Join(", ", reloadResult.UnloadableExtensions)}.");
            }

            ExtensionsReloaded?.Invoke(this, EventArgs.Empty);
            return reloadResult;
        }

        private static AssemblyLoadContext LoadExtension(FlowBloxProjectExtension extension, Dictionary<string, AssemblyLoadContext> loadContexts)
        {
            string directoryPath = extension.LocalExtensionDirectory;
            if (!loadContexts.ContainsKey(directoryPath))
            {
                _logger.Info($"Loading extension from {directoryPath}.");
                var loadContext = new FlowBloxProjectLoadContext(directoryPath);
                loadContexts.Add(directoryPath, loadContext);
                loadContext.LoadAssembliesFromDirectory();
                OnAfterExtensionLoaded(loadContext);
                _logger.Info($"Extension loaded successfully from {directoryPath}.");
                return loadContext;
            }

            return null;
        }

        private static void OnAfterExtensionLoaded(AssemblyLoadContext loadContext)
        {
            FlowBloxServiceLocator.Instance.RegisterServices(loadContext);
            FlowBlockCategory.InvokeRegistration();
            FlowBloxToolboxCategory.InvokeRegistration();
            FlowBloxOptions.GetOptionInstance().InitDefaults(false);
        }

        public event EventHandler<AssemblyLoadContext> BeforeUnloadExtension;

        private void UnloadExtension(string directoryPath, AssemblyLoadContext loadContext)
        {
            FlowBlockCategory.InvokeDeregistration(loadContext);
            FlowBloxServiceLocator.Instance.UnregisterServices(loadContext);
            BeforeUnloadExtension?.Invoke(this, loadContext);
            _logger.Info($"Unloading extension at {directoryPath}.");
            loadContext.Unload();
            _loadContexts.Remove(directoryPath);
            _logger.Info($"Extension unloaded successfully from {directoryPath}.");
        }

        public IEnumerable<T> CreateInstances<T>(Func<Type, bool> typeFilter = null) =>
            AppDomainInstanceFactory.CreateInstances<T>(_loadContexts.Values, typeFilter);

        private void CreateProjectDependendDataObjectsIfNotExist()
        {
            var dependendDataTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IProjectDependendData).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToList();

            foreach (var dependendDataType in dependendDataTypes)
            {
                if (!ProjectDependendDataObjects.Any(x => x.GetType() == dependendDataType))
                {
                    var instance = (IProjectDependendData)Activator.CreateInstance(dependendDataType);
                    ProjectDependendDataObjects.Add(instance);
                    _logger.Info($"Created instance of {dependendDataType.FullName} as project dependend data.");
                }
            }
        }

        /// <summary>
        /// <para>
        /// Completes the in-memory initialization of the project after deserialization.
        /// </para>
        /// 
        /// <para>
        /// Important:<br/>
        /// This method must only be called by FlowBloxProjectManager.<br/>
        /// It must not be invoked inside FromFile(), FromJsonContents() or any constructor.
        /// </para>
        /// 
        /// <para>
        /// The project itself represents only the data model.
        /// Lifecycle orchestration is handled by the manager.
        /// </para>
        /// </summary>
        internal void OnProjectLoaded()
        {
            _logger.Info("Loading project...");

            var options = FlowBloxOptions.GetOptionInstance();

            // Project dependent data
            CreateProjectDependendDataObjectsIfNotExist();

            List<IFlowBloxComponent> loadedComponents = new List<IFlowBloxComponent>();

            // FlowBlocks
            if (FlowBlocks != null)
            {
                foreach (var flowBlock in FlowBlocks.ExceptNull())
                {
                    FlowBloxRegistry.RegisterFlowBlock(flowBlock);
                    loadedComponents.Add(flowBlock);
                }
            }

            // ManagedObjects
            if (ManagedObjects != null)
            {
                foreach (var managedObject in ManagedObjects
                    .ExceptNull()
                    .Where(x => x is not FieldElement))
                {
                    FlowBloxRegistry.Register(managedObject);
                    loadedComponents.Add(managedObject);
                }
            }

            // UserFields
            if (UserFields != null)
            {
                foreach (var userField in UserFields)
                {
                    FlowBloxRegistry.Register(userField);
                    loadedComponents.Add(userField);
                }
            }

            // Trigger OnAfterLoad hooks
            foreach (var loadedComponent in loadedComponents)
            {
                loadedComponent.OnAfterLoad();
            }

            // Managed input files -> materialize input files if missing
            try
            {
                FlowBloxInputFileHelper.EnsureInputFilesExist(this);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to materialize managed input files.", ex);
            }

            _logger.Info("Project loaded successfully.");
        }

        internal void OnProjectClosed()
        {
            _logger.Info("Closing project...");

            var reloadResult = ReloadExtensions(_loadContexts.Keys);

            if (!reloadResult.Success)
                throw new ProjectExtensionsUnloadException(reloadResult.RemainingAssemblies, reloadResult.UnloadableExtensions);

            _logger.Info("Project closed successfully.");
        }

        // ============================================================
        // File layout
        // *.fbprj       => project json
        // *.fbdeps      => dependencies / extensions
        // *.fblocaldata => local machine/user data (ProjectSpaceGuid, local user-field values)
        // ============================================================

        private const string DependenciesFileSuffix = ".fbdeps";
        private const string LocalDataFileSuffix = ".fblocaldata";

        private static string BuildSidecarPath(string projectFile, string suffix)
        {
            return Path.Combine(
                Path.GetDirectoryName(projectFile),
                Path.GetFileNameWithoutExtension(projectFile) + suffix);
        }

        public static FlowBloxProject FromJsonContents(
            string projectJson,
            string extensionsJson,
            string projectSpaceGuid = null,
            int? projectSpaceVersion = null,
            string projectSpaceEndpointUri = null,
            string fileNameForAdjustments = null)
        {
            _logger.Info($"Loading project from JSON content (SpaceGuid='{projectSpaceGuid ?? ""}', Endpoint='{projectSpaceEndpointUri ?? ""}', File='{fileNameForAdjustments ?? ""}')");

            try
            {
                // Extensions
                Dictionary<string, AssemblyLoadContext> loadContexts = null;
                List<FlowBloxProjectExtension> extensions = null;

                if (!DisableExtensionLoading.IsActive && !string.IsNullOrWhiteSpace(extensionsJson))
                {
                    extensions = JsonConvert.DeserializeObject<List<FlowBloxProjectExtension>>(extensionsJson);

                    if (extensions != null && extensions.Count > 0)
                    {
                        loadContexts = LoadExtensions(extensions);
                        _logger.Info($"Extensions loaded from JSON. Count={extensions.Count}");
                    }
                }

                // Project JSON (same settings logic)
                var settings = JsonSettings.ProjectImport(loadContexts);

                var projectFileContent = projectJson ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(fileNameForAdjustments))
                    AdjustFileContentBeforeDeserialization(fileNameForAdjustments, ref projectFileContent);

                var project = JsonConvert.DeserializeObject<FlowBloxProject>(projectFileContent, settings);
                if (project == null)
                    throw new Exception("Failed to deserialize FlowBloxProject from JSON.");

                project.SetProjectFilePath(fileNameForAdjustments);

                if (extensions != null && extensions.Count > 0)
                {
                    project.Extensions.AddRange(extensions);

                    if (loadContexts != null)
                    {
                        foreach (var lc in loadContexts)
                            project._loadContexts.Add(lc.Key, lc.Value);
                    }
                }

                if (!string.IsNullOrWhiteSpace(projectSpaceGuid))
                    project.ProjectSpaceGuid = projectSpaceGuid;

                if (projectSpaceVersion.HasValue)
                    project.ProjectSpaceVersion = projectSpaceVersion;

                if (!string.IsNullOrWhiteSpace(projectSpaceEndpointUri))
                    project.ProjectSpaceEndpointUri = projectSpaceEndpointUri;

                _logger.Info("Project loaded successfully from JSON content.");
                return project;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to load project from JSON content.", ex);
                throw;
            }
        }

        public static FlowBloxProject FromFile(string fileName)
        {
            _logger.Info($"Loading project from file: {fileName}");
            try
            {
                // Dependencies / extensions JSON (*.fbdeps)
                var depsPath = BuildSidecarPath(fileName, DependenciesFileSuffix);
                string depsJson = !DisableExtensionLoading.IsActive && File.Exists(depsPath)
                    ? File.ReadAllText(depsPath)
                    : null;

                // Local data (*.fblocaldata)
                var localDataPath = BuildSidecarPath(fileName, LocalDataFileSuffix);
                FlowBloxProjectLocalData localData = null;

                if (File.Exists(localDataPath))
                {
                    var localJson = File.ReadAllText(localDataPath);
                    localData = JsonConvert.DeserializeObject<FlowBloxProjectLocalData>(localJson);
                }

                // Project JSON (*.fbprj)
                var projectFileContent = File.ReadAllText(fileName);

                // Central loading
                var project = FromJsonContents(
                    projectJson: projectFileContent,
                    extensionsJson: depsJson,
                    projectSpaceGuid: localData?.ProjectSpaceGuid,
                    projectSpaceVersion: localData?.ProjectSpaceVersion,
                    projectSpaceEndpointUri: localData?.ProjectSpaceEndpointUri,
                    fileNameForAdjustments: fileName);

                // Apply local user field values after registry is initialized
                project.ApplyLocalUserFieldValues(localData);

                _logger.Info($"Project loaded successfully from {fileName}");
                return project;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load project from file: {fileName}", ex);
                throw;
            }
        }

        private void ApplyLocalUserFieldValues(FlowBloxProjectLocalData localData)
        {
            if (localData?.LocalUserFieldValues == null || localData.LocalUserFieldValues.Count == 0)
                return;

            foreach (var field in UserFields)
            {
                if (field == null)
                    continue;

                if (field.UserField != true)
                    continue;

                // Only apply values for fields that are configured to store locally.
                // Note: This requires FieldElement.StoreValueLocally to exist as discussed.
                if (field.StoreValueLocally != true)
                    continue;

                if (localData.LocalUserFieldValues.TryGetValue(field.Name, out var value))
                {
                    // Set the persisted value (no runtime evaluation here).
                    field.StringValue = value;
                }
            }
        }

        private const string ProjectDirectoryPlaceholder = "${ProjectDir}";

        private static string MigrateJson(string json)
        {
            JObject rootObject = JObject.Parse(json);
            var migrationStrategies = FlowBloxServiceLocator.Instance.GetServices<IFlowBloxMigrationStrategy>();
            MigrateComponentList(rootObject, "FlowBlocks", migrationStrategies);
            MigrateComponentList(rootObject, "ManagedObjects", migrationStrategies);
            MigrateComponentList(rootObject, "UserFields", migrationStrategies);
            return rootObject.ToString();
        }

        private static void MigrateComponentList(JObject rootObject, string propertyName, IEnumerable<IFlowBloxMigrationStrategy> strategies)
        {
            if (!rootObject.TryGetValue(propertyName, out JToken token))
                return;

            JArray? jsonComponents = token switch
            {
                JArray arr => arr,
                JObject obj when obj.TryGetValue("$values", out var valuesToken) && valuesToken is JArray array => array,
                _ => null
            };

            if (jsonComponents == null)
                return;

            for (int i = 0; i < jsonComponents.Count; i++)
            {
                JObject componentJson = (JObject)jsonComponents[i];
                string typeName = componentJson["$type"]?.ToString();
                string versionString = componentJson["Version"]?.ToString() ?? "1.0.0.0";

                if (typeName == null)
                    continue;

                Type componentType = TypeNameHelper.FindTypeByFullOrSimpleName(typeName);
                if (componentType == null)
                    continue;

                Version componentVersion = new Version(versionString);
                Version assemblyVersion = componentType.Assembly.GetName().Version;

                if (componentVersion > assemblyVersion)
                    continue;

                var applicableStrategies = strategies
                    .Where(strategy => strategy.ComponentType.IsAssignableFrom(componentType) &&
                                       strategy.Version >= componentVersion)
                    .OrderBy(strategy => strategy.Version)
                    .ToList();

                foreach (var strategy in applicableStrategies)
                {
                    strategy.Migrate(componentJson);
                }

                componentJson["Version"] = assemblyVersion.ToString();
            }
        }

        private static void AdjustFileContentBeforeDeserialization(string fileName, ref string json)
        {
            string directoryPath = Path.GetDirectoryName(fileName);
            json = json.Replace(ProjectDirectoryPlaceholder, directoryPath.Replace(@"\", @"\\"));
            json = MigrateJson(json);
        }

        private static void AdjustFileContentAfterSerialization(string fileName, ref string json)
        {
            string directoryPath = Path.GetDirectoryName(fileName);
            json = json.Replace(directoryPath.Replace(@"\", @"\\"), ProjectDirectoryPlaceholder);
        }

        // ============================================================
        // Project Space ZIP (still contains only project + extensions)
        // LocalData must NOT be part of ProjectSpace uploads.
        // ============================================================

        private const string ProjectSpaceProjectFileName = "project_file.json";
        private const string ProjectSpaceExtensionsFileName = "extension_file.json";

        private static (string ProjectJson, string ExtensionsJson) ExtractProjectSpaceZip(byte[] zipBytes)
        {
            using (var ms = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false))
            {
                var projectEntry = archive.GetEntry(ProjectSpaceProjectFileName);
                var extensionEntry = archive.GetEntry(ProjectSpaceExtensionsFileName);

                if (projectEntry == null)
                    throw new Exception($"ZIP does not contain '{ProjectSpaceProjectFileName}'.");

                string ReadEntry(ZipArchiveEntry e)
                {
                    using (var s = e.Open())
                    using (var r = new StreamReader(s, Encoding.UTF8))
                        return r.ReadToEnd();
                }

                var projectJson = ReadEntry(projectEntry);
                var extensionsJson = extensionEntry != null ? ReadEntry(extensionEntry) : null;

                return (projectJson, extensionsJson);
            }
        }

        public static async Task<FlowBloxProject> FromProjectSpaceGuidAsync(string projectSpaceGuid, string userToken, FlowBloxWebApiService webApi)
        {
            return await FromProjectSpaceGuidAsync(projectSpaceGuid, null, userToken, webApi);
        }

        public static async Task<FlowBloxProject> FromProjectSpaceGuidAsync(
            string projectSpaceGuid,
            int? projectSpaceVersion,
            string userToken,
            FlowBloxWebApiService webApi)
        {
            var versionText = projectSpaceVersion.HasValue ? projectSpaceVersion.Value.ToString() : "latest";
            _logger.Info($"Loading project from Project Space. Guid={projectSpaceGuid}, Version={versionText}");

            if (string.IsNullOrWhiteSpace(projectSpaceGuid))
                throw new ArgumentException("projectSpaceGuid is required.", nameof(projectSpaceGuid));

            try
            {
                // Fetch metadata (usually still "project by guid"; ok for both latest and stable)
                var remoteResp = await webApi.GetProjectAsync(new FbProjectRequest { Guid = projectSpaceGuid }, userToken);
                if (!remoteResp.Success || remoteResp.ResultObject == null)
                    throw new InvalidOperationException("Project not found in Project Space.");

                // Fetch project content
                ApiResponse<string> contentResp;

                if (projectSpaceVersion.HasValue)
                {
                    // Version
                    contentResp = await webApi.GetProjectVersionContentAsync(
                        userToken,
                        Guid.Parse(projectSpaceGuid),
                        projectSpaceVersion.Value);
                }
                else
                {
                    // Latest
                    contentResp = await webApi.GetProjectContentAsync(
                        userToken,
                        Guid.Parse(projectSpaceGuid));
                }

                if (contentResp == null || !contentResp.Success)
                    throw new InvalidOperationException(
                        $"Failed to retrieve project content from Project Space. Version={versionText}. {contentResp?.ErrorMessage}");

                if (string.IsNullOrWhiteSpace(contentResp.ResultObject))
                    throw new Exception($"Project content is missing in Project Space response. Version={versionText}.");

                var zipBytes = Convert.FromBase64String(contentResp.ResultObject);
                var extracted = ExtractProjectSpaceZip(zipBytes);

                // Load project + extensions. No local data involved for ProjectSpace.
                var project = FromJsonContents(
                    projectJson: extracted.ProjectJson,
                    extensionsJson: extracted.ExtensionsJson,
                    projectSpaceGuid: projectSpaceGuid,
                    projectSpaceVersion: projectSpaceVersion,
                    projectSpaceEndpointUri: webApi.BaseUrl,
                    fileNameForAdjustments: null);

                project.ProjectSpaceGuid = projectSpaceGuid;

                _logger.Info($"Project loaded successfully from Project Space. Guid={projectSpaceGuid}, Version={versionText}");
                return project;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load project from Project Space. Guid={projectSpaceGuid}, Version={versionText}", ex);
                throw;
            }
        }

        public void RefreshOrderedTopLevelCollectionsForSerialization()
        {
            var ordered = ProjectSerializationOrdering.CreateOrderedTopLevelCollections(FlowBloxRegistry);
            UserFields = ordered.UserFields;
            ManagedObjects = ordered.ManagedObjects;
            FlowBlocks = ordered.FlowBlocks;
        }

        private string CreateProjectSpaceZipBase64()
        {
            // Collect current state before export and order by dependency for clean top-level JSON references.
            RefreshOrderedTopLevelCollectionsForSerialization();

            // Serialize project + extensions
            var projectFileContent = JsonConvert.SerializeObject(this, JsonSettings.ProjectExport());
            var extensionsFileContent = JsonConvert.SerializeObject(Extensions ?? new List<FlowBloxProjectExtension>());

            using (var memoryStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    // project_file.json
                    var projectEntry = archive.CreateEntry(ProjectSpaceProjectFileName, CompressionLevel.Optimal);
                    using (var entryStream = projectEntry.Open())
                    using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                    {
                        writer.Write(projectFileContent);
                    }

                    // extension_file.json
                    var extensionEntry = archive.CreateEntry(ProjectSpaceExtensionsFileName, CompressionLevel.Optimal);
                    using (var entryStream = extensionEntry.Open())
                    using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                    {
                        writer.Write(extensionsFileContent);
                    }
                }

                return Convert.ToBase64String(memoryStream.ToArray());
            }
        }

        public async Task<ApiResponse> SaveToProjectSpaceAsync(
            string projectGuid,
            string userToken,
            FlowBloxWebApiService webApi,
            bool generateNewProjectGuid = false)
        {
            if (string.IsNullOrWhiteSpace(projectGuid))
            {
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = "ProjectGuid is missing."
                };
            }

            var previousProjectGuid = ProjectGuid;
            try
            {
                if (generateNewProjectGuid)
                    ProjectGuid = Guid.NewGuid();

                // Create ZIP payload (project + extensions only)
                var base64Zip = CreateProjectSpaceZipBase64();

                var request = new FbProjectChangeRequest
                {
                    ProjectGuid = projectGuid,
                    ContentBase64 = base64Zip
                };

                var result = await webApi.UpdateProjectAsync(userToken, request);
                if (result?.Success == true)
                {
                    ProjectSpaceGuid = projectGuid;
                    ProjectSpaceEndpointUri = webApi.BaseUrl;
                    if (generateNewProjectGuid)
                        OnProjectGuidChanged(previousProjectGuid, ProjectGuid);
                }
                else if (generateNewProjectGuid)
                {
                    ProjectGuid = previousProjectGuid;
                }

                return result;
            }
            catch (Exception ex)
            {
                if (generateNewProjectGuid)
                    ProjectGuid = previousProjectGuid;

                FlowBloxLogManager.Instance.GetLogger().Exception(ex);
                return new ApiResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public void Save(string fileName, bool generateNewProjectGuid = false)
        {
            _logger.Info($"Saving project to file: {fileName}");
            var previousProjectGuid = ProjectGuid;
            try
            {
                if (generateNewProjectGuid)
                    ProjectGuid = Guid.NewGuid();

                // Collect current registry state and order by dependency for clean top-level JSON references.
                RefreshOrderedTopLevelCollectionsForSerialization();

                // Project file (*.fbprj)
                var projectFileContent = JsonConvert.SerializeObject(this, JsonSettings.ProjectExport());
                AdjustFileContentAfterSerialization(fileName, ref projectFileContent);
                File.WriteAllText(fileName, projectFileContent);

                // Dependencies (*.fbdeps)
                var depsPath = BuildSidecarPath(fileName, DependenciesFileSuffix);
                JsonHelper.SerializeToFile(depsPath, Extensions ?? new List<FlowBloxProjectExtension>());

                // Local data (*.fblocaldata)
                var localDataPath = BuildSidecarPath(fileName, LocalDataFileSuffix);
                var localData = BuildLocalDataForSave();
                JsonHelper.SerializeToFile(localDataPath, localData);

                SetProjectFilePath(fileName);

                _logger.Info($"Project saved successfully to {fileName}");
            }
            catch (Exception ex)
            {
                if (generateNewProjectGuid)
                    ProjectGuid = previousProjectGuid;

                _logger.Error($"Failed to save project to file: {fileName}", ex);
                throw;
            }

            if (generateNewProjectGuid)
                OnProjectGuidChanged(previousProjectGuid, ProjectGuid);
        }
        private void OnProjectGuidChanged(Guid oldProjectGuid, Guid newProjectGuid)
        {
            if (oldProjectGuid == newProjectGuid)
                return;

            try
            {
                ProjectGuidChanged?.Invoke(this, new ProjectGuidChangedEventArgs(oldProjectGuid, newProjectGuid));
            }
            catch (Exception ex)
            {
                _logger.Error("A project GUID changed event handler failed.", ex);
            }
        }

        internal void SetProjectFilePath(string fileName)
        {
            ProjectFilePath = string.IsNullOrWhiteSpace(fileName)
                ? string.Empty
                : Path.GetFullPath(fileName);
        }

        private FlowBloxProjectLocalData BuildLocalDataForSave()
        {
            var localData = new FlowBloxProjectLocalData
            {
                ProjectSpaceGuid = ProjectSpaceGuid,
                ProjectSpaceVersion = ProjectSpaceVersion,
                ProjectSpaceEndpointUri = ProjectSpaceEndpointUri
            };

            // Persist only user field values that are configured to store locally.
            // Note: This requires FieldElement.StoreValueLocally to exist.
            var userFields = FlowBloxRegistry.GetUserFields();
            foreach (var field in userFields)
            {
                if (field == null)
                    continue;

                if (field.UserField != true)
                    continue;

                if (field.StoreValueLocally != true)
                    continue;

                localData.LocalUserFieldValues[field.Name] = field.StringValue ?? string.Empty;
            }

            return localData;
        }
    }
}
