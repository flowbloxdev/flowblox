using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Components.Knowledge;

public enum VectorStoreBackend
{
    [Display(Name = "VectorStoreBackend_EmbeddedSqlite", ResourceType = typeof(FlowBloxTexts))]
    EmbeddedSqlite,
    [Display(Name = "Qdrant")]
    Qdrant,
    [Display(Name = "Pinecone")]
    Pinecone,
    [Display(Name = "Chroma")]
    Chroma
}
