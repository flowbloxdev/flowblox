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

    [Display(Name = "VectorTable_SourceFilter", Description = "VectorTable_SourceFilter_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
    [FlowBloxAssociation]
    [FlowBloxItemsSource(Method = nameof(GetSourceFilters), DisplayMember = nameof(Name))]
    public IReadableTable SourceFilter { get; set; }

    [Required]
    [Display(Name = "VectorTable_SourceIdColumn", Description = "VectorTable_SourceIdColumn_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
    public string SourceIdColumn { get; set; } = "SourceId";

    private CancellationToken _cancellationToken;

    public IEnumerable<VectorStore> GetStores() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<VectorStore>();
    public IEnumerable<TextEmbeddingProviderBase> GetEmbeddingProviders() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<TextEmbeddingProviderBase>();
    public IEnumerable<IReadableTable> GetSourceFilters() => FlowBloxRegistryProvider.GetRegistry()
        .GetManagedObjects<IReadableTable>()
        .Where(table => table != this && table is not VectorTable);
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
        activeSources ??= ReadSourceFilter();
        var question = this.Resolve(table => table.Question, required: true);
        var vectors = Embeddings.EmbedAsync([question], _cancellationToken).GetAwaiter().GetResult();
        return Store.Search(vectors.Single(), Embeddings, this.Resolve(table => table.TopK), activeSources, _cancellationToken);
    }

    private ISet<string> ReadSourceFilter()
    {
        if (SourceFilter == null)
            return null;

        if (SourceFilter is VectorTable)
            throw new InvalidOperationException("A vector search table cannot be used as a source filter.");

        var column = this.Resolve(table => table.SourceIdColumn, required: true);
        var data = SourceFilter.Read();
        if (!data.Columns.Contains(column))
            throw new InvalidOperationException($"The source filter does not contain column '{column}'.");

        return data.Rows.Cast<DataRow>()
            .Select(row => row[column] == DBNull.Value ? null : Convert.ToString(row[column]))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .ToHashSet(StringComparer.Ordinal);
    }

    public void AddDataSourceChangedListener(Action listener) { }
    public void RemoveDataSourceChangedListener(Action listener) { }
}
