using System.Text;
using System.Text.RegularExpressions;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Util.LocalAI;

/// <summary>
/// Prepares and applies project and block configuration without downloading or executing a model.
/// </summary>
public sealed class LocalAISetup
{
    private readonly FlowBloxProject _project;
    private readonly BaseFlowBlock _block;
    private readonly LocalAIScriptMetadata _metadata;
    private readonly Func<string, string> _readScript;
    private readonly LocalAISetupOptions _options;
    private byte[] _script = [];
    private FlowBloxInputFile _inputFile;
    private bool _prepared;

    public FieldElement ProviderField { get; private set; }

    public LocalAISetup(
        FlowBloxProject project,
        BaseFlowBlock block,
        LocalAIModel model,
        Func<string, string> readScript = null,
        LocalAISetupOptions options = null)
    {
        _project = project;
        _block = block == null ? null : FlowBloxRegistryProvider.GetRegistry().Reload(block);
        _metadata = LocalAIScriptCatalog.Scripts.Single(script => script.Model == model);
        _readScript = readScript ?? (name => DistributedDataCatalog.ReadContent(DistributedDataCatalog.PythonType, name));
        _options = options ?? new LocalAISetupOptions();
    }

    public string ScriptName => _metadata.ScriptName;

    public string ModelFolder => _metadata.ModelFolder;

    public static bool CanConfigureBlock(FlowBloxProject project, BaseFlowBlock block)
    {
        if (project == null || block == null)
            return false;

        var registry = FlowBloxRegistryProvider.GetRegistry();
        var effectiveBlock = registry.Reload(block);
        var original = registry.GetOriginalRef(effectiveBlock) as BaseFlowBlock;
        return registry.GetFlowBlocks().Contains(effectiveBlock)
            && project.FlowBloxRegistry.GetFlowBlocks().Contains(original);
    }

    public void Prepare()
    {
        _prepared = false;
        if (!CanConfigureBlock(_project, _block)
            || !LocalAIScriptCatalog.Supports(_metadata, _block))
            throw new InvalidOperationException(Text("LocalAISetup_IncompatibleBlock"));

        var script = _readScript(ScriptName);
        if (string.IsNullOrWhiteSpace(script))
            throw new InvalidOperationException(Text("LocalAISetup_MissingScript"));
        _script = Encoding.UTF8.GetBytes(script.TrimStart('\uFEFF'));

        var modelFolder = _block is OnnxGenAIFlowBlock genAI ? genAI.ModelFolder : ((OnnxQAFlowBlock)_block).ModelFolder;
        var provider = _block is OnnxGenAIFlowBlock genAIProvider ? genAIProvider.AiExecutionProvider : ((OnnxQAFlowBlock)_block).AiExecutionProvider;
        if (!_options.OverwriteBlockModel && !string.IsNullOrWhiteSpace(modelFolder))
            throw new InvalidOperationException(Text("LocalAISetup_BlockModelConflict"));
        if (!_options.OverwriteBlockProvider && (provider.HasValue ||
            _block.PropertySelectedFieldMap.TryGetValue(nameof(OnnxGenAIFlowBlock.AiExecutionProvider), out var selectedProvider) &&
            !string.IsNullOrWhiteSpace(selectedProvider)))
            throw new InvalidOperationException(Text("LocalAISetup_BlockProviderConflict"));

        var files = _project.InputFiles
            .Where(file => string.Equals(file.FileName, ScriptName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Count > 1)
            throw new InvalidOperationException(Text("LocalAISetup_DuplicateScripts"));
        _inputFile = files.SingleOrDefault();

        var fields = _project.FlowBloxRegistry.GetUserFields().ToList();
        var scriptMapping = Regex.Match(_inputFile?.Command ?? string.Empty, "--provider\\s+\"(?<field>\\$User::[^\"]+)\"")
            .Groups["field"].Value;
        var sharedProvider = fields.FirstOrDefault(field =>
            string.Equals(field.FullyQualifiedName, scriptMapping, StringComparison.OrdinalIgnoreCase));
        _block.PropertySelectedFieldMap.TryGetValue(nameof(OnnxGenAIFlowBlock.AiExecutionProvider), out var mapping);
        ProviderField = fields.FirstOrDefault(field =>
            string.Equals(field.FullyQualifiedName, mapping, StringComparison.OrdinalIgnoreCase))
            ?? sharedProvider
            ?? fields.FirstOrDefault(field => string.Equals(field.Name, "OnnxProvider", StringComparison.OrdinalIgnoreCase));
        if (sharedProvider != null && ProviderField != sharedProvider && !_options.OverwriteScript)
            throw new InvalidOperationException(Text("LocalAISetup_SharedProviderConflict"));
        if (ProviderField != null && !IsCompatibleProvider(ProviderField) && !_options.OverwriteProviderInput)
            throw new InvalidOperationException(string.Format(Text("LocalAISetup_ProviderConflict"), ProviderField.Name));

        if (_inputFile != null && !_options.OverwriteScript &&
            !InputFileMatches(BuildCommand(ProviderField?.FullyQualifiedName ?? "$User::OnnxProvider")))
            throw new InvalidOperationException(Text("LocalAISetup_ScriptConflict"));

        _prepared = true;
    }

    public bool ConfigureProvider()
    {
        EnsurePrepared();
        var existed = ProviderField != null;
        if (ProviderField == null)
        {
            ProviderField = new FieldElement(FieldTypes.Text)
            {
                Name = "OnnxProvider",
                UserField = true,
                UserFieldType = UserFieldTypes.Input,
                Index = _project.FlowBloxRegistry.GetUserFields().Select(field => field.Index ?? -1).DefaultIfEmpty(-1).Max() + 1
            };
            ProviderField.OnAfterCreate();
            ProviderField.OnAfterLoad();
            _project.FlowBloxRegistry.RegisterManagedObject(ProviderField);
        }

        if (_options.OverwriteProviderInput && !IsCompatibleProvider(ProviderField))
        {
            ProviderField.UserField = true;
            ProviderField.UserFieldType = UserFieldTypes.Input;
            ProviderField.FieldType = new TypeElement { FieldType = FieldTypes.Text };
            ProviderField.IsPassword = false;
        }
        if (string.IsNullOrWhiteSpace(ProviderField.DefaultValue) || !IsProvider(ProviderField.DefaultValue))
            ProviderField.DefaultValue = nameof(AiExecutionProviders.Default);
        ProviderField.IsRequiredAtRuntimeStartup = true;
        ProviderField.LoVType = LoVType.PossibleValues;
        ProviderField.ListOfValues = [];
        foreach (var provider in new[] { AiExecutionProviders.Default, AiExecutionProviders.CUDA, AiExecutionProviders.DirectML })
        {
            if (!ProviderField.ListOfValues.Any(value =>
                string.Equals(value.Value, provider.ToString(), StringComparison.OrdinalIgnoreCase)))
                ProviderField.ListOfValues.Add(new ValueItem
                {
                    Value = provider.ToString(),
                    DisplayName = provider == AiExecutionProviders.Default ? Text("LocalAISetup_CPU") : provider.ToString()
                });
        }

        // The field is project-owned, but the active editor also needs to resolve its mapping.
        var registry = FlowBloxRegistryProvider.GetRegistry();
        if (!ReferenceEquals(registry, _project.FlowBloxRegistry) && !registry.GetManagedObjects().Contains(ProviderField))
            registry.RegisterManagedObject(ProviderField);
        return existed;
    }

    public bool ConfigureInputFile()
    {
        EnsurePrepared();
        if (ProviderField == null)
            throw new InvalidOperationException(Text("LocalAISetup_ProviderRequired"));
        var command = BuildCommand(ProviderField.FullyQualifiedName);
        var unchanged = InputFileMatches(command);
        if (_inputFile == null)
        {
            _inputFile = new FlowBloxInputFile { RelativePath = ScriptName };
            _project.InputFiles.Add(_inputFile);
        }
        _inputFile.ContentBytes = _script;
        _inputFile.Command = command;
        _inputFile.ExecuteBeforeRuntime = true;
        _inputFile.SyncMode = FlowBloxInputFileSyncMode.AlwaysOverwrite;
        return unchanged;
    }

    private string BuildCommand(string providerReference)
    {
        var command = $"python \"$InputFile::Path\" --provider \"{providerReference}\"";
        command += _metadata.IsGenAI
            ? $" --model-root-directory \"$Options::{OptionsConstants.AIOnnxGenAIModelRootDirectory}\" --model-directory-link \"{ModelFolder}\""
            : $" --model-root-directory \"$Options::{OptionsConstants.AIOnnxQAModelRootDirectory}\"";
        return command;
    }

    private bool InputFileMatches(string command) => _inputFile != null
            && _inputFile.Command == command
            && _inputFile.ExecuteBeforeRuntime
            && _inputFile.SyncMode == FlowBloxInputFileSyncMode.AlwaysOverwrite
            && _inputFile.ContentBytes.SequenceEqual(_script);

    public void ConfigureBlock()
    {
        ConfigureBlockProvider();
        ConfigureBlockModel();
    }

    public void ConfigureBlockProvider()
    {
        EnsurePrepared();
        if (ProviderField == null)
            throw new InvalidOperationException(Text("LocalAISetup_ProviderRequired"));
        _block.PropertySelectedFieldMap[nameof(OnnxGenAIFlowBlock.AiExecutionProvider)] = ProviderField.FullyQualifiedName;
        if (_block is OnnxGenAIFlowBlock genAI)
        {
            genAI.AiExecutionProvider = AiExecutionProviders.Default;
        }
        else if (_block is OnnxQAFlowBlock qa)
            qa.AiExecutionProvider = AiExecutionProviders.Default;
    }

    public void ConfigureBlockModel()
    {
        EnsurePrepared();
        if (_block is OnnxGenAIFlowBlock genAI)
        {
            genAI.ModelFolder = ModelFolder;
            genAI.UseChatTemplate = true;
            genAI.ChatTemplate = _metadata.ChatTemplate;
        }
        else if (_block is OnnxQAFlowBlock qa)
        {
            qa.ModelFolder = ModelFolder;
        }
    }

    private static bool IsProvider(string value) =>
        Enum.TryParse<AiExecutionProviders>(value, true, out var provider)
        && provider is AiExecutionProviders.Default or AiExecutionProviders.CUDA or AiExecutionProviders.DirectML
        && string.Equals(value, provider.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool IsCompatibleProvider(FieldElement field) =>
        field.IsUserInputField() && field.FieldType?.FieldType == FieldTypes.Text && !field.IsPassword &&
        (string.IsNullOrWhiteSpace(field.DefaultValue) || IsProvider(field.DefaultValue));

    private void EnsurePrepared()
    {
        if (!_prepared)
            throw new InvalidOperationException(Text("LocalAISetup_NotPrepared"));
    }

    private static string Text(string key) => FlowBloxResourceUtil.GetLocalizedString(key, typeof(FlowBloxTexts));
}
