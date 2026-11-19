using System.ComponentModel.DataAnnotations;
using System.Data;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.Components.Knowledge;

[Display(Name = "VectorTable_DisplayName", Description = "VectorTable_Description", ResourceType = typeof(FlowBloxTexts))]
public class VectorTable : ManagedObject, IReadableTable
{
    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_cog, 16, SKColors.MediumPurple);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_cog, 32, SKColors.MediumPurple);

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
    [Display(Name = "VectorTable_Question", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    public string Question { get; set; }

    [Range(1, 100)]
    [Display(Name = "VectorTable_TopK", Description = "VectorTable_TopK_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
    public int TopK { get; set; } = 8;

    private CancellationToken _cancellationToken;

    public IEnumerable<VectorStore> GetStores() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<VectorStore>();
    public IEnumerable<TextEmbeddingProviderBase> GetEmbeddingProviders() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<TextEmbeddingProviderBase>();
    public override List<string> GetDisplayableProperties() => [nameof(Name), nameof(Store), nameof(Embeddings)];

    public override void RuntimeStarted(BaseRuntime runtime)
    {
        _cancellationToken = runtime.GetCancellationToken();
        base.RuntimeStarted(runtime);
    }

    public bool CanRead(BaseRuntime runtime = null) => Store != null && Embeddings != null;
    public DataTable Read() => Read(null);

    public DataTable Read(ISet<string>? activeSources)
    {
        var question = this.Resolve(table => table.Question, required: true);
        var vectors = Embeddings.EmbedAsync([question], _cancellationToken).GetAwaiter().GetResult();
        return Store.Search(vectors.Single(), Embeddings, this.Resolve(table => table.TopK), activeSources, _cancellationToken);
    }

    public void AddDataSourceChangedListener(Action listener) { }
    public void RemoveDataSourceChangedListener(Action listener) { }
}
