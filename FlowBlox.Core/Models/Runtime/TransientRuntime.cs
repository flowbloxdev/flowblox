using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Project;
using System.Linq;

namespace FlowBlox.Core.Models.Runtime
{
    public class TransientRuntime : BaseRuntime
    {
        public List<BaseFlowBlock> IncludedFlowBlocks { get; set; }

        public TransientRuntime(FlowBloxProject project) : base(project)
        {
            this.ExecutionFlowEnabled = false;
            this.DisableInterceptors = true;
            this.IncludedFlowBlocks = new List<BaseFlowBlock>();
        }

        public void InitializeRuntime(List<BaseFlowBlock> capturedFlowBlocks)
        {
            base.OnBeforeRuntimeStarted(capturedFlowBlocks);
        }

        public void ShutdownRuntime(List<BaseFlowBlock> capturedFlowBlocks)
        {
            base.OnAfterRuntimeFinished(capturedFlowBlocks);
        }

        protected override bool ShouldSkipValidation(BaseFlowBlock flowBlock)
        {
            if (flowBlock == null)
                return true;

            if (IncludedFlowBlocks == null)
                return false;

            return !IncludedFlowBlocks.ExceptNull().Any(x =>
                string.Equals(x.Name, flowBlock.Name, StringComparison.OrdinalIgnoreCase));
        }

        protected override bool ShouldExecuteInputFileStartupCommands() => false;

        // The test executor applies runtime-bound input values after initialization
        // and validates them before executing the test flow.
        protected override bool ShouldValidateUserFieldsAtRuntimeStartup() => false;
    }
}
