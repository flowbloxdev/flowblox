using System.Collections.ObjectModel;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;

namespace FlowBloxTest.FlowBlocks.Base;

public sealed class OutputSortProbeFlowBlock : BaseResultFlowBlock
{
    public ObservableCollection<FieldElement> ResultFields { get; } = new();
    public override List<FieldElement> Fields => ResultFields.ToList();
    public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.One;
    public override FlowBlockCategory GetCategory() => FlowBlockCategory.ControlFlow;
    public override bool Execute(BaseRuntime runtime, object data) => throw new NotSupportedException("Use Emit for output-generation tests.");

    public void Emit(BaseRuntime runtime, params string[][] rows)
    {
        GenerateResult(runtime, rows.Select(row => ResultFields
            .Select((field, index) => new KeyValuePair<FieldElement, string>(field, row[index]))
            .ToDictionary(pair => pair.Key, pair => pair.Value)));
    }
}
