using System.ComponentModel.DataAnnotations;
using System.Data;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.Components.Knowledge;

[Display(Name = "VectorSourceTable_DisplayName", Description = "VectorSourceTable_Description", ResourceType = typeof(FlowBloxTexts))]
public sealed class VectorSourceTable : ManagedObject, IReadableTable
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

    public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_cog, 16, SKColors.MediumPurple);
    public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.database_cog, 32, SKColors.MediumPurple);
    public override List<string> GetDisplayableProperties() => [nameof(Name), nameof(Store), nameof(Embeddings)];
    public IEnumerable<VectorStore> GetStores() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<VectorStore>();
    public IEnumerable<TextEmbeddingProviderBase> GetEmbeddingProviders() => FlowBloxRegistryProvider.GetRegistry().GetManagedObjects<TextEmbeddingProviderBase>();
    public bool CanRead(BaseRuntime runtime = null) => Store != null && Embeddings != null;
    public DataTable Read() => Store.GetSources(Embeddings);
    public void AddDataSourceChangedListener(Action listener) { }
    public void RemoveDataSourceChangedListener(Action listener) { }
}
