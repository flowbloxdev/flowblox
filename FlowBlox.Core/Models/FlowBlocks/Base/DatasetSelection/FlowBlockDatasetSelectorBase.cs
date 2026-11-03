using FlowBlox.Core.Models.FlowBlocks.Additions;

namespace FlowBlox.Core.Models.FlowBlocks.Base.DatasetSelection
{
    public abstract class FlowBlockDatasetSelectorBase
    {
        protected readonly Dictionary<BaseFlowBlock, HashSet<FlowBlockOut>> passedResults;
        protected readonly IList<InputBehaviorAssignment> inputBehaviorAssignments;

        public FlowBlockDatasetSelectorBase(
             Dictionary<BaseFlowBlock, HashSet<FlowBlockOut>> passedResults,
             IList<InputBehaviorAssignment> inputBehaviorAssignments)
        {
            this.passedResults = passedResults ?? throw new ArgumentNullException(nameof(passedResults));
            ArgumentNullException.ThrowIfNull(inputBehaviorAssignments);
            this.inputBehaviorAssignments = inputBehaviorAssignments.Where(assignment => assignment?.FlowBlock != null).ToList();
        }

        public abstract List<FlowBlockOutDataset> GetResults();

        protected bool IsValid(FlowBlockOutDataset item)
        {
            return item?.FieldValueMappings != null &&
                   item.FieldValueMappings.Any(x => !string.IsNullOrEmpty(x.Value));
        }

        protected InputBehaviorAssignment GetInputBehaviorAssignment(BaseResultFlowBlock underlyingFlowBlock)
        {
            return inputBehaviorAssignments.Single(y => y.FlowBlock == underlyingFlowBlock);
        }
    }
}
