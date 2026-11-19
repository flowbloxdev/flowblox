using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Knowledge;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.Components.Knowledge;

[Display(Name = "OnnxTextEmbeddingProvider_DisplayName", Description = "OnnxTextEmbeddingProvider_Description", ResourceType = typeof(FlowBloxTexts))]
public sealed class OnnxTextEmbeddingProvider : TextEmbeddingProviderBase
{
    private readonly object _sync = new();
    private OnnxEmbeddingModel _model;
    private BaseRuntime _runtime;

    [Required]
    [Display(Name = "OnnxTextEmbeddingProvider_ModelFolder", Description = "OnnxTextEmbeddingProvider_ModelFolder_Tooltip", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxUI(UISelectionButtons.FLD | UISelectionButtons.DI)]
    public string ModelFolder { get; set; } = "$Project::InputDirectory/embedding-model";

    [Display(Name = "OnnxRuntimeGenAIFlowBlock_AiExecutionProvider", Description = "OnnxRuntimeGenAIFlowBlock_AiExecutionProvider_Tooltip", ResourceType = typeof(FlowBloxTexts))]
    public AiExecutionProviders? AiExecutionProvider { get; set; }

    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.vector_polyline, 16, SKColors.Teal);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.vector_polyline, 32, SKColors.Teal);
    public override List<string> GetDisplayableProperties() => [nameof(Name), nameof(ModelFolder)];

    public override void RuntimeStarted(BaseRuntime runtime)
    {
        base.RuntimeStarted(runtime);
        _runtime = runtime;
    }

    public override void RuntimeFinished(BaseRuntime runtime)
    {
        lock (_sync)
        {
            _model?.Dispose();
            _model = null;
            _runtime = null;
        }
        base.RuntimeFinished(runtime);
    }

    public override string GetProfile()
    {
        lock (_sync)
            return GetModel().Profile;
    }

    public override IReadOnlyList<string> SplitText(string content, int size, int overlap)
    {
        lock (_sync)
            return GetModel().SplitText(content, size, overlap);
    }

    public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (texts.Count == 0)
                return Task.FromResult<IReadOnlyList<float[]>>([]);

            var model = GetModel();
            var vectors = new List<float[]>(texts.Count);
            foreach (var text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                vectors.Add(model.Embed(text, cancellationToken));
            }
            return Task.FromResult<IReadOnlyList<float[]>>(vectors);
        }
    }

    private OnnxEmbeddingModel GetModel()
    {
        if (_model != null)
            return _model;

        var modelFolder = this.Resolve(provider => provider.ModelFolder, required: true);
        if (_runtime == null)
            throw new InvalidOperationException("Start the runtime before using local ONNX embeddings.");

        try
        {
            OnnxEmbeddingModel.ValidateFiles(modelFolder);
            var providerName = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIOnnxProvider)?.Value?.Trim();
            var configuredProvider = Enum.TryParse<AiExecutionProviders>(providerName, true, out var configured)
                ? configured
                : AiExecutionProviders.Default;
            var provider = this.Resolve(owner => owner.AiExecutionProvider) ?? configuredProvider;
            FlowBloxOnnxRuntimeLoader.Instance.EnsureLoaded(provider, _runtime);
            _model = new OnnxEmbeddingModel(modelFolder);
            _runtime.Report($"Loaded local ONNX embedding model once for this runtime: {modelFolder}");
            return _model;
        }
        catch (Exception exception)
        {
            throw new ExecutionEnvironmentPrerequisiteException(
                "ONNX text embeddings", typeof(VectorStore), modelFolder, exception);
        }
    }
}
