using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;
using FlowBlox.Core.Logging;
using Newtonsoft.Json;

namespace FlowBlox.Core.Models.Runtime
{
    public class ProblemsTracer : IDisposable
    {
        private const int FlushIntervalMilliseconds = 2000;
        private readonly object _sync = new();
        private readonly string _problemTraceDir;
        private readonly string _baseTraceFilePath;
        private readonly Timer _flushTimer;
        private readonly List<PendingTraceSummary> _pendingTraceSummaries = new();
        private string _traceFilePath;
        private int _traceFileIndex;
        private ProblemTraceSummary _traceSummary;
        private readonly int _entryLimit = 500;
        private bool _writeRequired;
        private bool _disposed;

        public ProblemsTracer(BaseRuntime runtime)
        {
            _problemTraceDir = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsProblemTraceDir).Value;
            
            if (runtime is not FlowBloxRuntime fBRuntime)
                throw new NotSupportedException($"ProblemsTracer only supports instances of {nameof(FlowBloxRuntime)}.");

            _baseTraceFilePath = Path.Combine(
                _problemTraceDir,
                $"{Path.GetFileNameWithoutExtension(fBRuntime.RuntimeLogFileName)}_problems.json");
            
            _traceSummary = new ProblemTraceSummary();
            _traceFilePath = _baseTraceFilePath;
            _traceFileIndex = 0;
            _flushTimer = new Timer(
                _ => FlushIfRequired(),
                null,
                FlushIntervalMilliseconds,
                FlushIntervalMilliseconds);
        }

        public void AppendTrace(ProblemTrace trace)
        {
            if (trace == null)
                return;

            lock (_sync)
            {
                if (_disposed)
                    return;

                if (_traceSummary.Traces.Count >= _entryLimit)
                    RollTraceFile();

                _traceSummary.Traces.Add(trace);
                _writeRequired = true;
            }
        }

        public void Flush()
        {
            FlushIfRequired();
        }

        private void RollTraceFile()
        {
            if (_writeRequired)
            {
                AddOrReplacePendingTraceSummary(_traceFilePath, _traceSummary);
                _writeRequired = false;
            }

            _traceFileIndex++;
            _traceFilePath = $"{Path.GetFileNameWithoutExtension(_baseTraceFilePath)}_{_traceFileIndex}.json";
            _traceSummary = new ProblemTraceSummary();
        }

        private void FlushIfRequired()
        {
            List<PendingTraceSummary> traceSummaries;

            lock (_sync)
            {
                if (_disposed && !_writeRequired && _pendingTraceSummaries.Count == 0)
                    return;

                if (!_writeRequired && _pendingTraceSummaries.Count == 0)
                    return;

                traceSummaries = _pendingTraceSummaries.ToList();
                _pendingTraceSummaries.Clear();

                if (_writeRequired)
                {
                    traceSummaries.Add(new PendingTraceSummary(
                        _traceFilePath,
                        new ProblemTraceSummary
                        {
                            Traces = _traceSummary.Traces.ToList()
                        }));
                    _writeRequired = false;
                }
            }

            foreach (var traceSummary in traceSummaries)
                SerializeTraceSummary(traceSummary.TraceFilePath, traceSummary.TraceSummary);
        }

        private void SerializeTraceSummary(string traceFilePath, ProblemTraceSummary traceSummary)
        {
            try
            {
                if (!Directory.Exists(_problemTraceDir))
                    Directory.CreateDirectory(_problemTraceDir);

                var json = JsonConvert.SerializeObject(traceSummary, Formatting.Indented);
                File.WriteAllText(traceFilePath, json);
            }
            catch (Exception exception)
            {
                lock (_sync)
                    AddOrReplacePendingTraceSummary(traceFilePath, traceSummary);

                FlowBloxLogManager.Instance.GetLogger().Error("Failed to write problem trace summary.", exception);
            }
        }

        private void AddOrReplacePendingTraceSummary(string traceFilePath, ProblemTraceSummary traceSummary)
        {
            var existingIndex = _pendingTraceSummaries.FindIndex(x => string.Equals(
                x.TraceFilePath,
                traceFilePath,
                StringComparison.OrdinalIgnoreCase));

            var pending = new PendingTraceSummary(traceFilePath, traceSummary);
            if (existingIndex >= 0)
                _pendingTraceSummaries[existingIndex] = pending;
            else
                _pendingTraceSummaries.Add(pending);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            FlushIfRequired();
            _flushTimer.Dispose();
        }

        private sealed record PendingTraceSummary(string TraceFilePath, ProblemTraceSummary TraceSummary);
    }
}
