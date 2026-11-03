using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Models.Project
{
    public sealed class FlowBloxGenerationStrategyPlaceholderElement
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Placeholder => FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.GenerationStrategy, Key);
    }
}
