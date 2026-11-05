using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;

namespace FlowBlox.Core.Interceptors
{
    public class RuntimeBacktraceInterceptor : RuntimeInterceptorBase
    {
        public delegate void ProblemTraceCreatedEventHandler(BaseRuntime runtime, ProblemTrace problemTrace);

        public event ProblemTraceCreatedEventHandler ProblemTraceCreated;

        private readonly Lazy<ProblemsTracer> _problemsTracer;

        public RuntimeBacktraceInterceptor()
        {
            _problemsTracer = new Lazy<ProblemsTracer>(() => new ProblemsTracer(this.Runtime));
        }

        public override void NotifyWarning(BaseFlowBlock baseFlowBlock, string message, Exception exception = null)
        {
            var trace = new ProblemTrace
            {
                Name = baseFlowBlock.Name,
                Criticality = "Warning",
                Message = message,
                Exception = exception
            };

            AppendFieldValues(baseFlowBlock, trace);

            AppendTrace(trace);

            base.NotifyWarning(baseFlowBlock, message, exception);
        }

        public override void NotifyError(BaseFlowBlock baseFlowBlock, string message, Exception exception = null)
        {
            var trace = new ProblemTrace
            {
                Name = baseFlowBlock.Name,
                Criticality = "Error",
                Message = message,
                Exception = exception
            };

            AppendFieldValues(baseFlowBlock, trace);

            AppendTrace(trace);

            base.NotifyError(baseFlowBlock, message, exception);
        }

        private void AppendTrace(ProblemTrace trace)
        {
            // Create trace output
            _problemsTracer.Value.AppendTrace(trace);

            // Notify external components
            ProblemTraceCreated?.Invoke(Runtime, trace);
        }

        public override void NotifyRuntimeFinished()
        {
            if (_problemsTracer.IsValueCreated)
                _problemsTracer.Value.Dispose();

            base.NotifyRuntimeFinished();
        }

        private int? _maxFieldValueLength;
        private int MaxFieldValueLength
        {
            get
            {
                if (_maxFieldValueLength == null)
                {
                    var optionMaxFieldValueLength = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.RuntimeProblemTracingMaxFieldValueLength);
                    var maxFieldValueLengthString = optionMaxFieldValueLength.Value;

                    int maxFieldValueLength;
                    if (int.TryParse(maxFieldValueLengthString, out maxFieldValueLength))
                        _maxFieldValueLength = maxFieldValueLength;
                    else
                        _maxFieldValueLength = 200;
                }

                return _maxFieldValueLength.Value;
            }
        }

        private void AppendFieldValues(BaseFlowBlock baseFlowBlock, ProblemTrace trace)
        {
            if (baseFlowBlock == null || trace == null)
                return;

            var registry = FlowBloxRegistryProvider.GetRegistry();
            var startFlowBlock = registry.GetStartFlowBlock();
            if (startFlowBlock == null)
                return;

            var orderedFlowBlocks = registry
                .GetFlowBlocksRecursiveOrderedByExecutionFlow(startFlowBlock)
                .ToList();

            var currentFlowBlockIndex = orderedFlowBlocks.FindIndex(flowBlock => ReferenceEquals(flowBlock, baseFlowBlock));
            if (currentFlowBlockIndex <= 0)
                return;

            foreach (var item in orderedFlowBlocks
                .Take(currentFlowBlockIndex)
                .Select((flowBlock, index) => new { FlowBlock = flowBlock, ExecutionIndex = index }))
            {
                if (item.FlowBlock is not BaseResultFlowBlock resultFlowBlock)
                    continue;

                foreach (var field in resultFlowBlock.Fields)
                {
                    if (field == null)
                        continue;

                    trace.FieldValues.Add(new FieldValue(
                        field.FullyQualifiedName,
                        field.StringValue,
                        TextHelper.ShortenString(field.StringValue, MaxFieldValueLength, false),
                        item.ExecutionIndex));
                }
            }
        }
    }
}
