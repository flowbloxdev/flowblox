using System.Globalization;

namespace FlowBlox.Core.Util.Downloads
{
    /// <summary>
    /// Reports transfer phases and progress without logging every stream read or process output line.
    /// Create one instance per transfer.
    /// </summary>
    public sealed class TransferProgressReporter
    {
        private readonly Action<string> _report;
        private readonly TimeProvider _timeProvider;
        private readonly object _sync = new();
        private string? _stage;
        private double? _lastPercent;
        private DateTimeOffset _lastReport;
        private bool _completed;

        public TransferProgressReporter(Action<string> report, TimeProvider? timeProvider = null)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public void Report(string stage, double? fraction = null)
        {
            ReportCore(stage, fraction, null);
        }

        public void ReportBytes(long received, long? total)
        {
            double? fraction = total > 0 ? (double)received / total.Value : null;
            ReportCore("Downloading", fraction, received);
        }

        public void ReportPosition(TimeSpan position)
        {
            ReportCore("Processing media", null, null, position);
        }

        public void Complete()
        {
            lock (_sync)
            {
                if (_completed)
                    return;

                _completed = true;
                _report("Transfer completed.");
            }
        }

        private void ReportCore(string stage, double? fraction, long? bytes, TimeSpan? position = null)
        {
            lock (_sync)
            {
                if (_completed)
                    return;

                double? percent = fraction.HasValue && double.IsFinite(fraction.Value)
                    ? Math.Clamp(fraction.Value * 100, 0, 100)
                    : null;
                var now = _timeProvider.GetUtcNow();
                var changedStage = !string.Equals(_stage, stage, StringComparison.Ordinal);
                var finishedStage = percent >= 100 && _lastPercent.GetValueOrDefault() < 100;
                var restartedStage = percent.HasValue && _lastPercent >= 100 && percent < 100;
                var elapsed = now - _lastReport >= TimeSpan.FromSeconds(2);
                var advanced = !percent.HasValue || !_lastPercent.HasValue || percent.Value - _lastPercent.Value >= 5;

                if (!changedStage && !restartedStage && !finishedStage && !(elapsed && advanced))
                    return;

                // Phase-only notifications are emitted once, even during long post-processing.
                if (!changedStage && !percent.HasValue && !bytes.HasValue && !position.HasValue)
                    return;

                _stage = stage;
                _lastPercent = percent;
                _lastReport = now;
                var detail = percent.HasValue
                    ? percent.Value.ToString("0", CultureInfo.InvariantCulture) + "%"
                    : bytes.HasValue
                        ? (bytes.Value / 1048576d).ToString("0.0", CultureInfo.InvariantCulture) + " MiB received"
                        : position.HasValue
                            ? position.Value.ToString("c", CultureInfo.InvariantCulture) + " processed"
                            : "started";
                _report(stage + ": " + detail + ".");
            }
        }
    }
}
