using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AI.TokenSelector;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Provider.Toolbox;
using Microsoft.ML.OnnxRuntimeGenAI;
using Newtonsoft.Json;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace FlowBlox.Core.Models.FlowBlocks.AI
{
    [FlowBloxUIGroup("OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", 10)]
    [Display(Name = "OnnxRuntimeGenAIFlowBlock_DisplayName", Description = "OnnxRuntimeGenAIFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("OnnxGenAIFlowBlock_SpecialExplanation_ManagedResource", Icon = SpecialExplanationIcon.Information)]
    [FlowBloxSpecialExplanation("OnnxGenAIFlowBlock_SpecialExplanation_ModelProvisioning", Icon = SpecialExplanationIcon.Hint)]
    public class OnnxGenAIFlowBlock : BaseSingleResultFlowBlock
    {
        public const string DefaultModelFolderName = "Phi-4-mini-instruct-onnx";

        private static readonly FlowBloxRuntimeModelCache<GenAIModelSession> ModelCache = new();

        private Model _model;
        private Tokenizer _tokenizer;
        private string _resolvedModelFolder;

        #region Tab: Default

        [Required]
        [Display(Name = "OnnxRuntimeGenAIFlowBlock_ModelFolder", Description = "OnnxRuntimeGenAIFlowBlock_ModelFolder_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.FLD | UISelectionButtons.DI)]
        [FlowBloxFieldSelection(AllowedFieldSelectionModes = FieldSelectionModes.ProjectProperties)]
        [FlowBloxOpenFromFileSystem(InitialDirectoryMethod = nameof(GetInitialModelDirectory))]
        public string ModelFolder { get; set; }

        [Display(Name = "OnnxRuntimeGenAIFlowBlock_Prompt", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.AIPromptTemplates))]
        [Required]
        public string Prompt { get; set; }

        #endregion

        #region Tab: Extended settings

        [JsonIgnore]
        [DeepCopierIgnore]
        [Display(Name = "OnnxRuntimeGenAIFlowBlock_AiExecutionProvider", Description = "OnnxRuntimeGenAIFlowBlock_AiExecutionProvider_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(ReadOnly = true)]
        [Required]
        public AiExecutionProviders AiExecutionProvider
        {
            get
            {
                var providerOption = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIOnnxProvider);
                if (providerOption == null)
                    return AiExecutionProviders.Default;

                var providerName = providerOption.Value?.Trim();
                if (string.IsNullOrWhiteSpace(providerName))
                    return AiExecutionProviders.Default;

                if (Enum.TryParse<AiExecutionProviders>(providerName, ignoreCase: true, out var parsed))
                    return parsed;

                return AiExecutionProviders.Default;
            }
        }

        [Display(Name = "OnnxRuntimeGenAIFlowBlock_TokenSelectionStrategy", Description = "OnnxRuntimeGenAIFlowBlock_TokenSelectionStrategy_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        public TokenSelectionStrategy TokenSelectionStrategy { get; set; }

        [Display(Name = "OnnxRuntimeGenAIFlowBlock_MaxNewTokens", Description = "OnnxRuntimeGenAIFlowBlock_MaxNewTokens_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings",
            ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public int MaxNewTokens { get; set; }

        [Display(Name = "OnnxRuntimeGenAIFlowBlock_Temperature", Description = "OnnxRuntimeGenAIFlowBlock_Temperature_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public float Temperature { get; set; }

        [Display(Name = "OnnxRuntimeGenAIFlowBlock_TopP", Description = "OnnxRuntimeGenAIFlowBlock_TopP_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public float TopP { get; set; }

        [Display(Name = "OnnxRuntimeGenAIFlowBlock_TopK", Description = "OnnxRuntimeGenAIFlowBlock_TopK_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        public int TopK { get; set; }

        [Display(Name = "OnnxRuntimeGenAIFlowBlock_UseChatTemplate", Description = "OnnxRuntimeGenAIFlowBlock_UseChatTemplate_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        public bool UseChatTemplate { get; set; }

        [Display(Name = "OnnxRuntimeGenAIFlowBlock_SystemPrompt", Description = "OnnxRuntimeGenAIFlowBlock_SystemPrompt_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 7)]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.AIPromptTemplates))]
        public string SystemPrompt { get; set; }

        [ActivationCondition(MemberName = nameof(UseChatTemplate), Value = true)]
        [ConditionallyRequired]
        [Display(Name = "OnnxRuntimeGenAIFlowBlock_ChatTemplate", Description = "OnnxRuntimeGenAIFlowBlock_ChatTemplate_Tooltip",
            GroupName = "OnnxRuntimeGenAIFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 8)]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.ChatTemplates))]
        public string ChatTemplate { get; set; }

        #endregion

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_long, 16, SKColors.CadetBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.text_long, 32, SKColors.CadetBlue);
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.AI;

        public OnnxGenAIFlowBlock()
        {
            ModelFolder = $"$Options::{OptionsConstants.AIOnnxGenAIModelRootDirectory}\\{DefaultModelFolderName}";
            MaxNewTokens = 200;

            Temperature = 0.0f;
            TopP = 1.0f;
            TopK = 1;

            UseChatTemplate = true;
            SystemPrompt = "You are a helpful assistant.";
            ChatTemplate = FlowBloxToolboxResourceProvider.GetToolboxElementContent(
                ToolboxConstants.ChatTemplatesCategory,
                ToolboxConstants.Phi4MiniInstructChatTemplateName);
        }

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement(
                "AI.Onnx.GenAI.ModelRootDirectory",
                @"$Options::Paths.GlobalDataDir\onnx\genai",
                "User-specific root directory for ONNX Runtime GenAI models.",
                OptionElement.OptionType.Text,
                "ONNX GenAI: Model root directory",
                isPlaceholderEnabled: true));

            base.OptionsInit(defaults);
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var modelFolder = GetResolvedModelFolder();

                if (string.IsNullOrWhiteSpace(modelFolder) || !Directory.Exists(modelFolder))
                {
                    var failure = new ExecutionPrerequisiteException("ONNX GenAI", typeof(OnnxGenAIFlowBlock), modelFolder);
                    CreateNotification(runtime, OnnxRuntimeGenAiNotifications.ModelFolderMissing, failure.NotificationMessage, failure);
                    GenerateResult(runtime);
                    return;
                }

                if (string.IsNullOrWhiteSpace(Prompt))
                {
                    CreateNotification(runtime, OnnxRuntimeGenAiNotifications.PromptMissing);
                    GenerateResult(runtime);
                    return;
                }

                string userPrompt = this.Resolve(owner => owner.Prompt);
                string systemPrompt = this.Resolve(owner => owner.SystemPrompt);

                string finalPrompt = this.Resolve(x => x.UseChatTemplate)
                    ? ApplyChatTemplate(systemPrompt, userPrompt)
                    : userPrompt;

                string result;
                try
                {
                    result = RunGenAI(runtime, modelFolder, finalPrompt, this.Resolve(x => x.MaxNewTokens));
                }
                catch (Exception ex)
                {
                    CreateNotification(runtime, OnnxRuntimeGenAiNotifications.ExecutionFailed, ex);
                    GenerateResult(runtime);
                    return;
                }

                runtime.Report($"ONNX Runtime GenAI result: {result}");
                GenerateResult(runtime, result);
            });
        }

        private string ApplyChatTemplate(string systemPrompt, string userPrompt)
        {
            var chatTemplate = this.Resolve(owner => owner.ChatTemplate);
            if (string.IsNullOrEmpty(chatTemplate))
                throw new ArgumentNullException(nameof(this.ChatTemplate));

            return chatTemplate
                .Replace("{SystemPrompt}", systemPrompt ?? string.Empty)
                .Replace("{UserPrompt}", userPrompt ?? string.Empty);
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            base.RuntimeStarted(runtime);
            EnsureModelInitialized(runtime);
        }

        private void EnsureModelInitialized(BaseRuntime runtime)
        {
            try
            {
                InitializeModel(runtime);
            }
            catch (Exception exception)
            {
                var failure = new ExecutionPrerequisiteException(
                    "ONNX GenAI", typeof(OnnxGenAIFlowBlock), this.Resolve(owner => owner.ModelFolder), exception);
                CreateNotification(runtime, OnnxRuntimeGenAiNotifications.SetupRequired, failure.NotificationMessage, failure);
                throw failure;
            }
        }

        private void InitializeModel(BaseRuntime runtime)
        {
            // Ensure ONNX runtime native binaries are loaded:
            FlowBloxOnnxRuntimeLoader.Instance.EnsureLoaded(AiExecutionProvider, runtime);

            // Ensure GenAI native binaries are loaded:
            FlowBloxOnnxRuntimeGenAiLoader.Instance.EnsureLoaded(AiExecutionProvider, runtime);

            var modelFolder = ResolveModelFolder();
            _resolvedModelFolder = modelFolder;

            if (string.IsNullOrWhiteSpace(modelFolder))
                throw new InvalidOperationException("ONNX Runtime GenAI initialization failed: ModelFolder is null or empty.");

            if (!Directory.Exists(modelFolder))
                throw new InvalidOperationException($"ONNX Runtime GenAI initialization failed: ModelFolder does not exist: '{modelFolder}'");

            try
            {
                var modelSession = ModelCache.Open(
                    runtime,
                    modelFolder,
                    () => CreateModelSession(runtime, modelFolder),
                    out var alreadyOpen);
                _model = modelSession.Model;
                _tokenizer = modelSession.Tokenizer;

                if (alreadyOpen)
                {
                    runtime.Report(
                        $"Inference session is already open; using cached session for FlowBlock '{Name}' " +
                        $"and model folder: {modelFolder}");
                }
            }
            catch (Exception ex)
            {
                _model = null;
                _tokenizer = null;
                throw new InvalidOperationException($"ONNX Runtime GenAI initialization failed while loading model from '{modelFolder}'.", ex);
            }
        }

        private static GenAIModelSession CreateModelSession(BaseRuntime runtime, string modelFolder)
        {
            runtime.Report($"Loading ONNX model from folder: {modelFolder}");

            Model model = null;
            Tokenizer tokenizer = null;
            try
            {
                model = new Model(modelFolder);
                tokenizer = new Tokenizer(model);
                return new GenAIModelSession(model, tokenizer);
            }
            catch
            {
                tokenizer?.Dispose();
                model?.Dispose();
                throw;
            }
        }

        private string ResolveModelFolder() => 
            this.Resolve(owner => owner.ModelFolder);

        private string GetInitialModelDirectory()
        {
            var directory = FlowBloxOptions.GetOptionInstance()
                .GetOption(OptionsConstants.AIOnnxGenAIModelRootDirectory)?.Value;
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                ? directory
                : string.Empty;
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            if (!string.IsNullOrWhiteSpace(_resolvedModelFolder))
            {
                var closed = ModelCache.Close(runtime, _resolvedModelFolder);
                runtime.Report(closed
                    ? $"Closed cached ONNX GenAI inference session after FlowBlock '{Name}' finished."
                    : $"Cached ONNX GenAI inference session was already closed when FlowBlock '{Name}' finished.");
            }

            _tokenizer = null;
            _model = null;
            _resolvedModelFolder = null;

            base.RuntimeFinished(runtime);
        }

        private sealed class GenAIModelSession : IDisposable
        {
            public GenAIModelSession(
                Model model,
                Tokenizer tokenizer)
            {
                Model = model;
                Tokenizer = tokenizer;
            }

            public Model Model { get; }
            public Tokenizer Tokenizer { get; }

            public void Dispose()
            {
                Tokenizer.Dispose();
                Model.Dispose();
            }
        }

        private string GetResolvedModelFolder()
        {
            return !string.IsNullOrWhiteSpace(_resolvedModelFolder)
                ? _resolvedModelFolder
                : ResolveModelFolder();
        }

        private string RunGenAI(BaseRuntime runtime, string modelFolder, string prompt, int maxNewTokens)
        {

            using var stream = _tokenizer.CreateStream();

            // Encode -> Sequences
            using var inputSeq = _tokenizer.Encode(prompt);
            if (inputSeq == null || inputSeq.NumSequences <= 0)
                return string.Empty;

            // Try to get prompt length
            int promptLen = 0;
            try
            {
                var promptTokens = inputSeq[0];
                promptLen = promptTokens.Length;
            }
            catch
            {
                // Some bindings expose sequence differently
                // Keep promptLen=0 and use fallback below
                promptLen = 0;
            }

            using var genParams = new GeneratorParams(_model);

            // max_length = prompt + newTokens
            // If promptLen is unknown, use a safe cap
            int maxLength = (promptLen > 0)
                ? (promptLen + Math.Max(0, maxNewTokens))
                : Math.Max(256, maxNewTokens + 2048);

            genParams.SetSearchOption("max_length", (double)maxLength);

            ApplySearchOptions(genParams);

            using var generator = new Generator(_model, genParams);

            // attach prompt sequences to generator
            generator.AppendTokenSequences(inputSeq);

            var sb = new StringBuilder(capacity: Math.Max(256, maxNewTokens * 4));

            // We decode only newly generated tokens (skip prompt tokens)
            int lastEmittedLen = 0;
            while (!generator.IsDone())
            {
                generator.GenerateNextToken();

                var seq = generator.GetSequence(0);
                if (seq == null || seq.Length == 0)
                    continue;

                // First time: if promptLen is 0 (unknown), assume entire first seq is prompt+first gen token.
                // We still avoid duplicating by tracking lastEmittedLen.
                int start = Math.Max(promptLen, lastEmittedLen);

                if (seq.Length <= start)
                {
                    lastEmittedLen = seq.Length;
                    continue;
                }

                for (int i = start; i < seq.Length; i++)
                {
                    int token = seq[i];
                    sb.Append(stream.Decode(token));
                }

                lastEmittedLen = seq.Length;

                // hard stop: if user sets MaxNewTokens and generator doesn't early-stop
                if (maxNewTokens > 0 && promptLen > 0)
                {
                    int generatedSoFar = Math.Max(0, seq.Length - promptLen);
                    if (generatedSoFar >= maxNewTokens)
                        break;
                }
            }

            return sb.ToString();
        }

        private void ApplySearchOptions(GeneratorParams genParams)
        {
            // - If Temperature <= 0 or strategy is ArgMax => deterministic
            // - Else sampling with top_k/top_p/temperature
            bool deterministic =
                this.Resolve(x => x.TokenSelectionStrategy) == TokenSelectionStrategy.ArgMax ||
                this.Resolve(x => x.Temperature) <= 0.0f;

            if (deterministic)
            {
                genParams.SetSearchOption("do_sample", false);
                genParams.SetSearchOption("top_k", 1.0);
                genParams.SetSearchOption("top_p", 1.0);
                genParams.SetSearchOption("temperature", 0.0);
                return;
            }

            genParams.SetSearchOption("do_sample", true);
            genParams.SetSearchOption("top_k", (double)Math.Max(1, this.Resolve(x => x.TopK)));
            genParams.SetSearchOption("top_p", (double)Math.Clamp(this.Resolve(x => x.TopP), 0.0f, 1.0f));
            genParams.SetSearchOption("temperature", (double)Math.Max(0.0f, this.Resolve(x => x.Temperature)));
        }

        public override List<string> GetDisplayableProperties()
        {
            var props = base.GetDisplayableProperties();
            props.Add(nameof(ModelFolder));
            return props;
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(OnnxRuntimeGenAiNotifications));
                return notificationTypes;
            }
        }
    }

    public enum OnnxRuntimeGenAiNotifications
    {
        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Model folder missing")]
        ModelFolderMissing,

        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Prompt is empty.")]
        PromptMissing,

        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Failed to execute prompt using ONNX Runtime GenAI.")]
        ExecutionFailed,

        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Setup required")]
        SetupRequired
    }
}
