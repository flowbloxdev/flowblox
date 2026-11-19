using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Knowledge;

namespace FlowBlox.Core.Models.Components.Knowledge;

[Display(Name = "TextEmbeddingProvider_DisplayName", ResourceType = typeof(FlowBloxTexts))]
public abstract class TextEmbeddingProviderBase : ManagedObject
{
    public abstract string GetProfile();

    public abstract Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken);

    public virtual IReadOnlyList<string> SplitText(string content, int size, int overlap) =>
        KnowledgeTextChunks.Split(content, size, overlap);
}
