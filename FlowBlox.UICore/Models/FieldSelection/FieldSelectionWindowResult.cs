using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.UICore.Enums;

namespace FlowBlox.UICore.Models.FieldSelection
{
    public sealed class FieldSelectionWindowResult
    {
        public FieldSelectionMode SelectionMode { get; set; }

        public List<FieldElement> SelectedFields { get; set; } = new List<FieldElement>();

        public List<FlowBloxProjectPropertyElement> SelectedProjectProperties { get; set; } = new List<FlowBloxProjectPropertyElement>();

        public List<OptionElement> SelectedOptions { get; set; } = new List<OptionElement>();

        public List<FlowBloxInputFilePlaceholderElement> SelectedInputFiles { get; set; } = new List<FlowBloxInputFilePlaceholderElement>();

        public List<FlowBloxGenerationStrategyPlaceholderElement> SelectedGenerationStrategyData { get; set; } = new List<FlowBloxGenerationStrategyPlaceholderElement>();

        public List<string> SelectedVaultReferences { get; set; } = new();

        public bool IsRequired { get; set; }
    }
}
