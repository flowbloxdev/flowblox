using Microsoft.SemanticKernel;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Base
{
    public sealed class AIChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        /// <summary>
        /// Marks the end of stable bootstrap content for provider-specific prompt-cache preparation.
        /// </summary>
        public bool IsBootstrapCacheBoundary { get; set; }
        /// <summary>
        /// Marks a reusable conversation prefix before the protected latest messages or maintenance suffix.
        /// </summary>
        public bool IsContextCacheBoundary { get; set; }
        public ChatMessageContent? SemanticContent { get; set; }
    }
}
