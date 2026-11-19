using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.Knowledge;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.Knowledge;

[Display(Name = "UpsertVectorSourceFlowBlock_DisplayName", Description = "UpsertVectorSourceFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
public sealed class UpsertVectorSourceFlowBlock : BaseSingleResultFlowBlock
{
    [Required]
    [Display(Name = "VectorStore_DisplayName", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    [FlowBloxAssociation]
    [FlowBloxItemsSource(Method = nameof(GetStores), DisplayMember = nameof(Name))]
    public VectorStore Store { get; set; }

    [Required]
    [Display(Name = "TextEmbeddingProvider_DisplayName", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    [FlowBloxAssociation]
    [FlowBloxItemsSource(Method = nameof(GetEmbeddingProviders), DisplayMember = nameof(Name))]
    public TextEmbeddingProviderBase Embeddings { get; set; }

    [Required]
    [Display(Name = "UpsertVectorSourceFlowBlock_SourceId", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    public string SourceId { get; set; }

    [Display(Name = "UpsertVectorSourceFlowBlock_Title", ResourceType = typeof(FlowBloxTexts), Order = 3)]
    public string Title { get; set; }

    [Display(Name = "UpsertVectorSourceFlowBlock_Content", ResourceType = typeof(FlowBloxTexts), Order = 4)]
    [FlowBloxTextBox(MultiLine = true)]
    public string Content { get; set; }

    [Required]
    [Display(Name = "UpsertVectorSourceFlowBlock_SourceIdResult", Description = "UpsertVectorSourceFlowBlock_SourceIdResult_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 102)]
    [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
    public FieldElement SourceIdResult { get; set; }

    public override List<FieldElement> Fields => SourceIdResult == null ? base.Fields : [.. base.Fields, SourceIdResult];

    public override void OnAfterCreate()
    {
        base.OnAfterCreate();
        SourceIdResult ??= FlowBloxRegistryProvider.GetRegistry().CreateField(this, fieldType: FieldTypes.Text);
        SourceIdResult.Name = "SourceId";
    }

    public IEnumerable<VectorStore> GetStores() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<VectorStore>();
    public IEnumerable<TextEmbeddingProviderBase> GetEmbeddingProviders() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<TextEmbeddingProviderBase>();
    public override FieldTypes DefaultResultFieldType => FieldTypes.Boolean;
    public override string DefaultResultFieldName => "Success";
    public override FlowBlockCategory GetCategory() => FlowBlockCategory.Persistence;
    public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
    public override List<string> GetDisplayableProperties() => [.. base.GetDisplayableProperties(), nameof(Store), nameof(Embeddings), nameof(SourceId)];
    public override List<Type> NotificationTypes => [.. base.NotificationTypes, typeof(IndexNotifications)];
    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_plus_outline, 16, SKColors.MediumPurple);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_plus_outline, 32, SKColors.MediumPurple);

    public override bool Execute(BaseRuntime runtime, object data)
    {
        return Invoke(runtime, data, () =>
        {
            var sourceId = this.Resolve(block => block.SourceId);
            try
            {
                var changed = Store.UpsertAsync(
                    sourceId,
                    this.Resolve(block => block.Title),
                    this.Resolve(block => block.Content),
                    Embeddings,
                    runtime.GetCancellationToken()).GetAwaiter().GetResult();
                runtime.Report(changed ? "Knowledge source indexed." : "Knowledge source unchanged; embeddings reused.");
                GenerateSourceResult(runtime, sourceId, true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && exception is not ExecutionEnvironmentPrerequisiteException)
            {
                runtime.Report("Failed to index a knowledge source.", FlowBloxLogLevel.Error, exception);
                CreateNotification(runtime, IndexNotifications.IndexFailed, exception);
                GenerateSourceResult(runtime, sourceId, false);
            }
        });
    }

    private void GenerateSourceResult(BaseRuntime runtime, string sourceId, bool success)
    {
        GenerateResult(runtime, new[]
        {
            new Dictionary<FieldElement, string>
            {
                [ResultField] = success ? "true" : "false",
                [SourceIdResult] = sourceId
            }
        });
    }

    public enum IndexNotifications
    {
        [FlowBloxNotification(NotificationType = NotificationType.Error)]
        [Display(Name = "Could not index source")]
        IndexFailed
    }
}
