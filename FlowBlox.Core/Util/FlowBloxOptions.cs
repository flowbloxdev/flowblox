using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Factories;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider.Project;
using System.Xml;

namespace FlowBlox.Core.Util
{
    public class FlowBloxOptions
    {
        private static FlowBloxOptions _instance = null;

        public SortedDictionary<string, OptionElement> OptionCollection { get; } = new SortedDictionary<string, OptionElement>();
       
        private FlowBloxOptions()
        {
            LoadOptions();
            InitDefaults(false);
        }

        private void LoadOptions()
        {
            string globalConfigurationXmlContent = File.Exists(GlobalPaths.GlobalConfigurationXml) ?
                File.ReadAllText(GlobalPaths.GlobalConfigurationXml) : string.Empty;

            XmlDocument xmlDocument = new XmlDocument();
            if (!globalConfigurationXmlContent.Equals(string.Empty))
            {
                xmlDocument.LoadXml(globalConfigurationXmlContent);

                XmlNodeList xnlOptions = xmlDocument.SelectNodes("//option_elements/option_element");

                foreach (XmlNode xnOption in xnlOptions)
                {
                    OptionElement optionElement = OptionElement.FromXml(xnOption);
                    OptionCollection[optionElement.Name] = optionElement;
                }
            }
        }

        public bool HasOption(string optionName)
        {
            if (OptionCollection.ContainsKey(optionName))
                return true;

            return false;
        }

        private string GetDefaultEditorPath()
        {
            string editorPath = string.Empty;

            foreach (string programFiles in new string[] { "%programfiles(x86)%", "%programfiles%" })
            {
                string nppPath = Environment.ExpandEnvironmentVariables(programFiles + @"\Notepad++\notepad++.exe");
                if (File.Exists(Environment.ExpandEnvironmentVariables(nppPath)))
                    editorPath = nppPath;
            }

            editorPath = editorPath.Equals(string.Empty) ? 
                @"C:\Windows\System32\notepad.exe" : 
                editorPath;

            return editorPath;
        }

        public void InitDefaults(bool overwrite)
        {
            // Parsing formats are owned by field and column type definitions.
            lock (_saveLock)
            {
                OptionCollection.Remove("Parsing.DateTimeFormat");
                OptionCollection.Remove("Parsing.DecimalSeparator");
                OptionCollection.Remove("Parsing.GroupSeparator");
            }

            var defaultOptions = new List<OptionElement>
            {
                new OptionElement("Account.LoginData", "", "The encrypted login data for the user account. This option stores the login token securely using encryption.", OptionElement.OptionType.Password),
                new OptionElement("Account.LicenseToken", "", "The encrypted license token for the user account. This option stores the license token securely using encryption.", OptionElement.OptionType.Text),
                new OptionElement("Editor.DefaultPath", GetDefaultEditorPath(), "Path to the default editor for directly editing or viewing data from FlowBlox. We recommend the free software Notepad++: https://notepad-plus-plus.org/", OptionElement.OptionType.Text),
                new OptionElement("UI.Style", "Professional", "Set your style here. Available styles by default: \"Default\", \"Professional\". To apply a new style, you must change this setting and restart FlowBlox.", OptionElement.OptionType.Text),
                new OptionElement("UI.Culture", "", "Optional UI culture to force language/formatting, for example 'de-DE' or 'en-US'. Leave empty to use the operating system culture.", OptionElement.OptionType.Text),
                new OptionElement("FieldView.MaxDisplayLength", "3000", "Maximum number of characters displayed per field value in the FieldView UI. Full content remains available when opening/editing the field value.", OptionElement.OptionType.Integer),
                new OptionElement("FieldView.ShowFlowBlock", "false", "Show or hide the Flow Block column in the FieldView.", OptionElement.OptionType.Boolean, "FieldView: Show Flow Block"),
                new OptionElement("FieldView.SingleLineFieldValues", "false", "Show field values in single-line mode in the FieldView.", OptionElement.OptionType.Boolean, "FieldView: 1 Line Values"),
                new OptionElement("TestDefinitionView.HideLastLayerNeighbours", "true", "Hide sibling flow blocks in the target flow block layer and their successor trees when editing a test definition in flow-block context.", OptionElement.OptionType.Boolean),
                new OptionElement("PropertyWindow.EnableApplySplitButton", "true", "Use a split button for Apply actions in the property window. If disabled, Apply and Apply without verification are displayed as separate buttons.", OptionElement.OptionType.Boolean, "Property window: Apply split button"),
                new OptionElement("PropertyWindow.EnableSaveSplitButton", "true", "Use a split button for Save actions in the property window. If disabled, Save and Save without verification are displayed as separate buttons.", OptionElement.OptionType.Boolean, "Property window: Save split button"),
                new OptionElement("PropertyWindow.ApplyButtonSelection", "ApplyWithoutVerification", "Action executed by the Apply split button. Possible values: Apply, ApplyWithoutVerification.", OptionElement.OptionType.Text),
                new OptionElement("PropertyWindow.SaveButtonSelection", "Save", "Action executed by the Save split button. Possible values: Save, SaveWithoutVerification.", OptionElement.OptionType.Text),
                new OptionElement("AI.ApprovedSkillProjects", string.Empty, "Skill projects approved for AI Assistant execution without further confirmation. One entry per line: filename for a distributed skill project template, full path for a custom project. Remove an entry to revoke approval.", OptionElement.OptionType.Text),

                new OptionElement("Paths.DocumentsDir", @"%userprofile%\Documents\FlowBlox", "Root directory for user documents managed by FlowBlox.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.LocalAppDataDir", @"%localappdata%\FlowBlox", "Root directory for local application data managed by FlowBlox.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.GlobalDataDir", @"%userprofile%\.flowblox", "User-specific root directory for cross-project FlowBlox data.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.ToolboxDir", @"$Options::Paths.DocumentsDir\toolbox", "Toolbox directory path.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.ToolboxUserFile", @"$Options::Paths.ToolboxDir\userToolbox.json", "Toolbox user file path.", OptionElement.OptionType.Text),
                new OptionElement("Paths.ProjectDir", @"$Options::Paths.DocumentsDir\projects", "Project directory path.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.InputDir", @"$Options::Paths.DocumentsDir\input", "Input directory path.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.OutputDir", @"$Options::Paths.DocumentsDir\output", "Output directory path.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.ExtensionsDir", @"$Options::Paths.LocalAppDataDir\extensions", "Extensions directory path.", OptionElement.OptionType.Text),
                new OptionElement("Paths.ProblemTraceDir", @"$Options::Paths.RuntimeLogDir\problems", "Problem trace directory path.", OptionElement.OptionType.Text),
                new OptionElement("Paths.RuntimeLogDir", @"$Options::Paths.LocalAppDataDir\logs\runtime", "Runtime log directory path.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.ApplicationLogDir", @"$Options::Paths.LocalAppDataDir\logs\application", "Application log directory path.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.ScheduledTasksDir", @"$Options::Paths.LocalAppDataDir\tasks", "Base directory for scheduled FlowBlox task request, response, and log files.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),
                new OptionElement("Paths.DeepCopierProtocolDir", @"$Options::Paths.LocalAppDataDir\copy_protocols", "Deep copier protocol directory.", OptionElement.OptionType.Text),
                new OptionElement("Paths.ToolboxCacheDir", @"$Options::Paths.LocalAppDataDir\toolbox", "Global toolbox cache directory path.", OptionElement.OptionType.Text),

                new OptionElement("Api.ExtensionServiceBaseUrl", GlobalUrls.FlowBloxPublicApiBaseUrl, "The URL for the REST API of the extension management system.", OptionElement.OptionType.Text),
                new OptionElement("Api.ProjectServiceBaseUrl", GlobalUrls.FlowBloxPublicApiBaseUrl, "The URL for the REST API of the project space.", OptionElement.OptionType.Text),
                new OptionElement("Updates.InstallerManifestUrl", GlobalUrls.FlowBloxInstallerManifestUrl, "The URL of the installer update manifest XML used by the desktop updater notifications.", OptionElement.OptionType.Text, "Updates: Installer Manifest URL"),

                new OptionElement("Runtime.AutoRestart", "false", "Should the runtime automatically restart once execution is completed?", OptionElement.OptionType.Boolean, "Auto Restart"),
                new OptionElement("Runtime.AutoRestart.CacheMode", "Keep", "Should the cache be cleared or kept in case of an automatic restart? Possible values: \"Keep\" or \"Clear\" Note: This option can also be changed after runtime start.", OptionElement.OptionType.Text),
                new OptionElement("Runtime.StepTimeunit", "20", "With this option, you can set the time unit between two element executions. (specified in milliseconds)", OptionElement.OptionType.Integer),
                new OptionElement("Runtime.Debugging.StepTimeunit", "75", "Debugging time unit between two element executions used by the runtime view. Set to 0 to use Runtime.StepTimeunit.", OptionElement.OptionType.Integer),
                new OptionElement("Runtime.Debugging.StepwiseExecution", "false", "With this option, you can set whether the runtime execution is paused after each element execution. Helpful for debugging.", OptionElement.OptionType.Boolean, "Debugging: Stepwise Execution"),
                new OptionElement("Runtime.Debugging.StopOnWarning", "false", "With this option, you can set whether the execution should be paused at every runtime warning. Helpful for debugging.", OptionElement.OptionType.Boolean, "Debugging: Stop on Warning"),
                new OptionElement("Runtime.Debugging.StopOnError", "true", "With this option, you can set whether the execution should be paused at every runtime error. Helpful for debugging.", OptionElement.OptionType.Boolean, "Debugging: Stop on Error"),
                new OptionElement("Runtime.Window.LogAction", "false", "Should the action be logged in the runtime window?", OptionElement.OptionType.Boolean, "Log Action"),
                new OptionElement("Runtime.Window.LogStatus", "true", "Should the status be logged in the runtime window?", OptionElement.OptionType.Boolean, "Log Status"),
                new OptionElement("Runtime.CacheMode", "AutoClear", "With this option, you can set how FlowBlox should behave before re-execution when extraction elements have already processed web pages from a past execution. Possible values: AskUser, AutoClear", OptionElement.OptionType.Text),
                new OptionElement("Runtime.ProblemTracing.MaxFieldValueLength", "200", "With this option you can specify the maximum field value length recorded. Set to a low value for performance/memory reasons.", OptionElement.OptionType.Integer),

                new OptionElement(SqlStatementSafetyGuard.AllowDestructiveStatementsOptionName, "false", "Allow destructive SQL statements such as DELETE, DROP, and TRUNCATE. Keep disabled unless such operations are explicitly intended.", OptionElement.OptionType.Boolean, "Allow destructive SQL statements"),

                new OptionElement("Grid.DefaultSize", "3000,1000", "When a new project is created, the grid size width, height is set to this value by default.", OptionElement.OptionType.Text),
                new OptionElement("Grid.ResetNotificationsOnRuntimeFinish", "true", "Controls whether flow-block notifications (warning/error) are reset when runtime execution finishes.", OptionElement.OptionType.Boolean),
                new OptionElement("MainPanel.DockSettings", "", "Stores the layout and visibility settings of all dockable panels within the main application window. This includes the default docking positions, size dimensions (width, height), and display states (shown/hidden) of each panel.", OptionElement.OptionType.Text),

                new OptionElement("Modifier.DefaultSeparator", ",", "If a modifier returns multiple result values, these values are separated by this delimiter.", OptionElement.OptionType.Text, isPlaceholderEnabled: true),

                new OptionElement("AI.Onnx.Provider", "Default", "Global ONNX execution provider used by the application. Allowed values: Default (CPU), CUDA, DirectML, OpenVINO. Changing this requires restarting the application.", OptionElement.OptionType.Text)
            };

            foreach(var optionsRegistration in FlowBloxServiceLocator.Instance.GetServices<IOptionsRegistration>())
            {
                optionsRegistration.OptionsInit(defaultOptions, [.. OptionCollection.Values]);
            }

            foreach (var component in
                FlowBloxProjectManager.Instance.ActiveProject?.CreateInstances<IFlowBloxComponent>() ??
                AppDomainInstanceFactory.CreateInstances<IFlowBloxComponent>())
            {
                component.OptionsInit(defaultOptions, [.. OptionCollection.Values]);
            }

            lock (_saveLock)
            {
                foreach (var option in defaultOptions)
                {
                    bool hasOptionValue = false;
                    if (OptionCollection.TryGetValue(option.Name, out OptionElement resolvedOption))
                        hasOptionValue = resolvedOption.HasStoredValue;

                    if (!hasOptionValue || overwrite)
                        OptionCollection[option.Name] = option;
                }
            }
        }

        public List<OptionElement> GetOptions()
        {
            return OptionCollection.Values.ToList();
        }

        private static readonly XmlWriterSettings _xmlWriterSettings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace
        };

        private readonly object _saveLock = new object();

        public void Save()
        {
            lock (_saveLock)
            {
                XmlDocument xmlDocument = new XmlDocument();
                XmlNode xnOptions = xmlDocument.CreateElement("option_elements");
                var optionElements = OptionCollection.Values.ToList();
                foreach (OptionElement optionElement in optionElements)
                {
                    xnOptions.AppendChild(optionElement.SaveXml(xmlDocument));
                }
                xmlDocument.AppendChild(xnOptions);

                string directory = Path.GetDirectoryName(GlobalPaths.GlobalConfigurationXml);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                try
                {
                    using (FileStream fileStream = new FileStream(GlobalPaths.GlobalConfigurationXml, FileMode.Create, FileAccess.Write))
                    using (XmlWriter writer = XmlWriter.Create(fileStream, _xmlWriterSettings))
                    {
                        xmlDocument.Save(writer);
                    }
                }
                catch (IOException e)
                {
                    FlowBloxLogManager.Instance.GetLogger().Exception(e);
                }
            }
        }

        public static bool HasOptionInstance => _instance != null;

        private static readonly object _lockObject = new object();

        public static FlowBloxOptions GetOptionInstance()
        {
            lock (_lockObject)
            {
                if (_instance == null)
                    _instance = new FlowBloxOptions();
            }

            return _instance;
        }

        public OptionElement GetOption(string name)
        {
            if (_instance.OptionCollection.TryGetValue(name, out var option))
                return option;

            return null;
        }

        internal static string ResolveOptionValue(OptionElement option)
        {
            ArgumentNullException.ThrowIfNull(option);

            var persistentValue = option.PersistentValue ?? string.Empty;
            if (_instance == null)
                return Environment.ExpandEnvironmentVariables(persistentValue);

            var resolvingOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(option.Name))
                resolvingOptions.Add(option.Name);

            return Environment.ExpandEnvironmentVariables(
                _instance.ResolveOptionPlaceholders(persistentValue, resolvingOptions));
        }

        private string ResolveOptionPlaceholders(string value, HashSet<string> resolvingOptions)
        {
            foreach (var option in OptionCollection.Values
                .Where(x => x.IsPlaceholderEnabled)
                .OrderByDescending(x => x.Name.Length))
            {
                var placeholder = FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Options, option.Name);
                if (!value.Contains(placeholder, StringComparison.Ordinal))
                    continue;

                if (resolvingOptions.Contains(option.Name))
                    continue;

                resolvingOptions.Add(option.Name);
                var replacement = ResolveOptionPlaceholders(option.PersistentValue, resolvingOptions);
                resolvingOptions.Remove(option.Name);
                value = value.Replace(placeholder, replacement, StringComparison.Ordinal);
            }

            return value;
        }
    }
}
