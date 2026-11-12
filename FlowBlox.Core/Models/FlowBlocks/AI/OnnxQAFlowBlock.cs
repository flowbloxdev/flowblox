using FlowBlox.Core.Extensions;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.AI.OnnxQA;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FlowBlox.Core.Models.FlowBlocks.AI
{
    [FlowBloxUIGroup("OnnxQAFlowBlock_Groups_ExtendedSettings", 10)]
    [Display(Name = "OnnxQAFlowBlock_DisplayName", Description = "OnnxQAFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("OnnxQAFlowBlock_SpecialExplanation_ModelFolder", Icon = SpecialExplanationIcon.Information)]
    [FlowBloxSpecialExplanation("OnnxQAFlowBlock_SpecialExplanation_ModelProvisioning", Icon = SpecialExplanationIcon.Hint)]
    [FlowBloxSpecialExplanation("OnnxQAFlowBlock_SpecialExplanation_Extractive", Icon = SpecialExplanationIcon.Hint)]
    public class OnnxQAFlowBlock : BaseSingleResultFlowBlock
    {
        public const string DefaultModelFolderName = "mdeberta-v3-base-squad2";

        private static readonly FlowBloxRuntimeModelCache<QAModelSession> ModelCache = new();

        private InferenceSession _session;
        private OnnxQAModelTokenizer _tokenizer;
        private string _resolvedModelFolder;

        #region Tab: Default

        [Required]
        [Display(Name = "OnnxQAFlowBlock_ModelFolder", Description = "OnnxQAFlowBlock_ModelFolder_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(Factory = UIFactory.Default, UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFolderSelection)]
        [FlowBloxFieldSelection(AllowedFieldSelectionModes = FieldSelectionModes.ProjectProperties)]
        [FlowBloxOpenFromFileSystem(InitialDirectoryMethod = nameof(GetInitialModelDirectory))]
        public string ModelFolder { get; set; }

        [Required]
        [Display(Name = "OnnxQAFlowBlock_Question", Description = "OnnxQAFlowBlock_Question_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxTextBox(MultiLine = false)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Question { get; set; }

        [Required]
        [Display(Name = "OnnxQAFlowBlock_Context", Description = "OnnxQAFlowBlock_Context_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxTextBox(MultiLine = true)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string Context { get; set; }

        #endregion

        #region Tab: Extended settings

        [JsonIgnore]
        [DeepCopierIgnore]
        [Required]
        [Display(Name = "OnnxQAFlowBlock_AiExecutionProvider", Description = "OnnxQAFlowBlock_AiExecutionProvider_Tooltip",
            GroupName = "OnnxQAFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, ReadOnly = true)]
        public AiExecutionProviders AiExecutionProvider
        {
            get
            {
                var providerName = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIOnnxProvider)?.Value?.Trim();
                return Enum.TryParse<AiExecutionProviders>(providerName, ignoreCase: true, out var provider)
                    ? provider
                    : AiExecutionProviders.Default;
            }
        }

        [Range(16, 4096)]
        [Display(Name = "OnnxQAFlowBlock_MaxSequenceLength", Description = "OnnxQAFlowBlock_MaxSequenceLength_Tooltip",
            GroupName = "OnnxQAFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int MaxSequenceLength { get; set; }

        [Range(0, 4095)]
        [Display(Name = "OnnxQAFlowBlock_DocumentStride", Description = "OnnxQAFlowBlock_DocumentStride_Tooltip",
            GroupName = "OnnxQAFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int DocumentStride { get; set; }

        [Range(1, 512)]
        [Display(Name = "OnnxQAFlowBlock_MaxAnswerLength", Description = "OnnxQAFlowBlock_MaxAnswerLength_Tooltip",
            GroupName = "OnnxQAFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int MaxAnswerLength { get; set; }

        [Display(Name = "OnnxQAFlowBlock_AllowNoAnswer", Description = "OnnxQAFlowBlock_AllowNoAnswer_Tooltip",
            GroupName = "OnnxQAFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public bool AllowNoAnswer { get; set; }

        [ActivationCondition(MemberName = nameof(AllowNoAnswer), Value = true)]
        [Display(Name = "OnnxQAFlowBlock_NoAnswerThreshold", Description = "OnnxQAFlowBlock_NoAnswerThreshold_Tooltip",
            GroupName = "OnnxQAFlowBlock_Groups_ExtendedSettings", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public float NoAnswerThreshold { get; set; }

        #endregion

        public OnnxQAFlowBlock()
        {
            ModelFolder = $"$Options::{OptionsConstants.AIOnnxQAModelRootDirectory}\\{DefaultModelFolderName}";
            MaxSequenceLength = 384;
            DocumentStride = 128;
            MaxAnswerLength = 30;
            NoAnswerThreshold = 0.0f;
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.comment_question, 16, SKColors.MediumVioletRed);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.comment_question, 32, SKColors.MediumVioletRed);
        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.AI;

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement(
                "AI.Onnx.QA.ModelRootDirectory",
                @"$Options::Paths.GlobalDataDir\onnx\qa",
                "User-specific root directory for ONNX question-answering models.",
                OptionElement.OptionType.Text,
                "ONNX QA: Model root directory",
                isPlaceholderEnabled: true));

            base.OptionsInit(defaults);
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            base.RuntimeStarted(runtime);

            FlowBloxOnnxRuntimeLoader.Instance.EnsureLoaded(AiExecutionProvider, runtime);
            _resolvedModelFolder = ResolveModelFolder();
            ValidateModelFolder(_resolvedModelFolder);

            try
            {
                var modelSession = ModelCache.Open(
                    runtime,
                    _resolvedModelFolder,
                    () => CreateModelSession(runtime, _resolvedModelFolder),
                    out var alreadyOpen);
                _session = modelSession.Session;
                _tokenizer = modelSession.Tokenizer;

                if (alreadyOpen)
                {
                    runtime.Report(
                        $"Inference session is already open; using cached session for FlowBlock '{Name}' " +
                        $"and model folder: {_resolvedModelFolder}");
                }
            }
            catch
            {
                _session = null;
                _tokenizer = null;
                throw;
            }
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                var modelFolder = !string.IsNullOrWhiteSpace(_resolvedModelFolder) ? _resolvedModelFolder : ResolveModelFolder();
                if (string.IsNullOrWhiteSpace(modelFolder) || !Directory.Exists(modelFolder))
                {
                    CreateNotification(runtime, OnnxQANotifications.ModelFolderMissing);
                    GenerateResult(runtime);
                    return;
                }

                var question = this.Resolve(owner => owner.Question);
                var context = this.Resolve(owner => owner.Context);
                if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(context))
                {
                    CreateNotification(runtime, OnnxQANotifications.QuestionOrContextMissing);
                    GenerateResult(runtime);
                    return;
                }

                try
                {
                    var result = PredictAnswer(question, context);
                    runtime.Report($"ONNX QA result: {result}");
                    GenerateResult(runtime, result);
                }
                catch (Exception ex)
                {
                    CreateNotification(runtime, OnnxQANotifications.ExecutionFailed, ex);
                    GenerateResult(runtime);
                }
            });
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            if (!string.IsNullOrWhiteSpace(_resolvedModelFolder))
            {
                var closed = ModelCache.Close(runtime, _resolvedModelFolder);
                runtime.Report(closed
                    ? $"Closed cached ONNX QA inference session after FlowBlock '{Name}' finished."
                    : $"Cached ONNX QA inference session was already closed when FlowBlock '{Name}' finished.");
            }

            _session = null;
            _tokenizer = null;
            _resolvedModelFolder = null;
            base.RuntimeFinished(runtime);
        }

        private QAModelSession CreateModelSession(BaseRuntime runtime, string modelFolder)
        {
            var modelPath = ResolveModelPath(modelFolder);
            runtime.Report($"Loading ONNX question answering model from folder: {modelFolder}");

            using var sessionOptions = CreateSessionOptions(runtime);
            var session = new InferenceSession(modelPath, sessionOptions);
            try
            {
                var tokenizer = OnnxQAModelTokenizer.Load(modelFolder);
                ValidateModelContract(session);
                return new QAModelSession(session, tokenizer);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        private string PredictAnswer(string question, string context)
        {
            if (_session == null || _tokenizer == null)
                throw new InvalidOperationException("The ONNX QA model is not initialized.");

            var questionTokens = _tokenizer.EncodeTokens(question);
            var contextTokens = _tokenizer.EncodeTokens(context);
            var contextCapacity = _tokenizer.GetAvailableContextTokenCount(questionTokens.Count, MaxSequenceLength);
            if (contextCapacity <= 0)
                throw new InvalidOperationException("The question is too long for the configured maximum sequence length.");
            if (DocumentStride >= contextCapacity)
                throw new InvalidOperationException("DocumentStride must be smaller than the number of context tokens in a window.");

            OnnxQASpanCandidate bestSpan = null;
            var bestNullScore = float.PositiveInfinity;
            var windowStep = Math.Max(1, contextCapacity - DocumentStride);

            for (var contextOffset = 0; contextOffset < contextTokens.Count; contextOffset += windowStep)
            {
                var contextCount = Math.Min(contextCapacity, contextTokens.Count - contextOffset);
                var window = _tokenizer.BuildWindow(questionTokens, contextTokens, contextOffset, contextCount);
                var (startLogits, endLogits) = RunModel(window);

                var nullScore = startLogits[0] + endLogits[0];
                bestNullScore = Math.Min(bestNullScore, nullScore);

                var candidate = OnnxQASpanSelector.SelectBestSpan(
                    startLogits,
                    endLogits,
                    window,
                    contextTokens,
                    MaxAnswerLength);
                if (candidate != null && (bestSpan == null || candidate.Score > bestSpan.Score))
                    bestSpan = candidate;

                if (contextOffset + contextCount >= contextTokens.Count)
                    break;
            }

            if (bestSpan == null ||
                (AllowNoAnswer && bestNullScore - bestSpan.Score > NoAnswerThreshold))
                return string.Empty;

            var start = Math.Clamp(bestSpan.CharacterStart, 0, context.Length);
            var end = Math.Clamp(bestSpan.CharacterEnd, start, context.Length);
            return context[start..end];
        }

        private (float[] StartLogits, float[] EndLogits) RunModel(OnnxQAEncodedWindow window)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", CreateTensor(window.InputIds)),
                NamedOnnxValue.CreateFromTensor("attention_mask", CreateTensor(window.AttentionMask))
            };

            if (_session.InputMetadata.ContainsKey("token_type_ids"))
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", CreateTensor(window.TokenTypeIds)));

            using var results = _session.Run(inputs);
            var startLogits = results.First(result => result.Name == "start_logits").AsEnumerable<float>().ToArray();
            var endLogits = results.First(result => result.Name == "end_logits").AsEnumerable<float>().ToArray();
            return (startLogits, endLogits);
        }

        private static DenseTensor<long> CreateTensor(IReadOnlyList<long> values)
        {
            var tensor = new DenseTensor<long>(new[] { 1, values.Count });
            for (var index = 0; index < values.Count; index++)
                tensor[0, index] = values[index];
            return tensor;
        }

        private SessionOptions CreateSessionOptions(BaseRuntime runtime)
        {
            var options = new SessionOptions();
            switch (AiExecutionProvider)
            {
                case AiExecutionProviders.OpenVINO:
                    options.AppendExecutionProvider_OpenVINO("GPU");
                    runtime.Report("Using OpenVINO Execution Provider (GPU).");
                    break;
                case AiExecutionProviders.DirectML:
                    options.AppendExecutionProvider_DML();
                    runtime.Report("Using DirectML Execution Provider.");
                    break;
                case AiExecutionProviders.CUDA:
                    options.AppendExecutionProvider_CUDA();
                    runtime.Report("Using CUDA Execution Provider.");
                    break;
                default:
                    runtime.Report("Using default CPU Execution Provider.");
                    break;
            }

            return options;
        }

        private static void ValidateModelContract(InferenceSession session)
        {
            foreach (var input in new[] { "input_ids", "attention_mask" })
            {
                if (!session.InputMetadata.ContainsKey(input))
                    throw new InvalidDataException($"The ONNX model does not expose the required input '{input}'.");
            }

            foreach (var output in new[] { "start_logits", "end_logits" })
            {
                if (!session.OutputMetadata.ContainsKey(output))
                    throw new InvalidDataException($"The ONNX model does not expose the required output '{output}'.");
            }
        }

        private sealed class QAModelSession : IDisposable
        {
            public QAModelSession(InferenceSession session, OnnxQAModelTokenizer tokenizer)
            {
                Session = session;
                Tokenizer = tokenizer;
            }

            public InferenceSession Session { get; }
            public OnnxQAModelTokenizer Tokenizer { get; }

            public void Dispose() => Session.Dispose();
        }

        private static void ValidateModelFolder(string modelFolder)
        {
            if (string.IsNullOrWhiteSpace(modelFolder))
                throw new InvalidOperationException("ONNX QA initialization failed: ModelFolder is null or empty.");
            if (!Directory.Exists(modelFolder))
                throw new DirectoryNotFoundException($"ONNX QA model folder does not exist: '{modelFolder}'.");
        }

        private static string ResolveModelPath(string modelFolder)
        {
            var preferredPath = Path.Combine(modelFolder, "model.onnx");
            if (File.Exists(preferredPath))
                return preferredPath;

            var models = Directory.GetFiles(modelFolder, "*.onnx", SearchOption.TopDirectoryOnly);
            return models.Length == 1
                ? models[0]
                : throw new FileNotFoundException("The model folder must contain model.onnx or exactly one ONNX file.");
        }

        private string ResolveModelFolder() => this.Resolve(owner => owner.ModelFolder);

        private string GetInitialModelDirectory()
        {
            var directory = FlowBloxOptions.GetOptionInstance()
                .GetOption(OptionsConstants.AIOnnxQAModelRootDirectory)?.Value;
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                ? directory
                : string.Empty;
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(ModelFolder));
            properties.Add(nameof(Question));
            return properties;
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var types = base.NotificationTypes;
                types.Add(typeof(OnnxQANotifications));
                return types;
            }
        }
    }

    public enum OnnxQANotifications
    {
        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "The ONNX QA model folder is missing.")]
        ModelFolderMissing,

        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Question or context is empty.")]
        QuestionOrContextMissing,

        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Failed to execute the ONNX question answering model.")]
        ExecutionFailed
    }
}
