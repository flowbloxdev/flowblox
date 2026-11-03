using FlowBlox.Core.Models.FlowBlocks.Additions;

namespace FlowBlox.Core.Models.FlowBlocks.Base.DatasetSelection
{
    public static class FlowBlockDatasetSelectorFactory
    {
        public static FlowBlockDatasetSelectorBase Create(
            Dictionary<BaseFlowBlock, HashSet<FlowBlockOut>> passedResults,
            IList<InputBehaviorAssignment> inputBehaviorAssignments)
        {
            if (passedResults == null)
                throw new ArgumentNullException(nameof(passedResults));

            if (inputBehaviorAssignments == null)
                throw new ArgumentNullException(nameof(inputBehaviorAssignments));

            inputBehaviorAssignments = inputBehaviorAssignments
                .Where(assignment => assignment?.FlowBlock != null)
                .ToList();

            var behaviors = inputBehaviorAssignments
                .Select(a => a.Behavior)
                .Distinct()
                .ToList();

            bool usesRowWise = behaviors.Contains(InputBehavior.RowWise) ||
                               behaviors.Contains(InputBehavior.RowWiseValid);

            bool usesCombine = behaviors.Contains(InputBehavior.Cross) ||
                               behaviors.Contains(InputBehavior.CrossValid);

            if (usesRowWise && usesCombine)
            {
                // RowWise + Combine (Cross) is invalid
                throw new InvalidOperationException(
                    "Invalid input behavior configuration: RowWise/RowWiseValid and Cross/CrossValid " +
                    "must not be used together.");
            }

            if (usesRowWise)
                return new FlowBlockRowWiseDatasetSelector(passedResults, inputBehaviorAssignments);

            // Default: "normal" combination selector (Cross + First/Last/etc.)
            return new FlowBlockCombinationDatasetSelector(passedResults, inputBehaviorAssignments);
        }
    }
}
