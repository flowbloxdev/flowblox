using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Logging;
using System;
using System.Diagnostics;
using System.Threading;

namespace FlowBlox.AppWindow.Contents
{
    internal sealed class RuntimeFocusUpdateThrottler : IDisposable
    {
        private readonly WpfPanel _dispatcher;
        private readonly Action<BaseFlowBlock> _applyFocus;
        private readonly int _throttleMilliseconds;
        private readonly object _sync = new();
        private readonly System.Threading.Timer _timer;

        private BaseFlowBlock _pendingFlowBlock;
        private bool _updateScheduled;
        private int _version;
        private long _lastUpdateTimestamp;
        private bool _disposed;

        public RuntimeFocusUpdateThrottler(
            WpfPanel dispatcher,
            Action<BaseFlowBlock> applyFocus,
            int throttleMilliseconds)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _applyFocus = applyFocus ?? throw new ArgumentNullException(nameof(applyFocus));
            _throttleMilliseconds = Math.Max(0, throttleMilliseconds);
            _timer = new System.Threading.Timer(Timer_Tick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Schedule(BaseFlowBlock flowBlock)
        {
            if (_disposed)
                return;

            if (!_dispatcher.InvokeRequired)
            {
                _lastUpdateTimestamp = Stopwatch.GetTimestamp();
                _applyFocus(flowBlock);
                return;
            }

            var delay = 0;
            int version;
            lock (_sync)
            {
                _pendingFlowBlock = flowBlock;
                if (_updateScheduled)
                    return;

                _updateScheduled = true;
                version = _version;
                delay = GetUpdateDelayMilliseconds();
            }

            if (delay <= 0)
            {
                BeginInvoke(version);
                return;
            }

            _timer.Change(delay, Timeout.Infinite);
        }

        public void ClearPending()
        {
            lock (_sync)
            {
                _pendingFlowBlock = null;
                _updateScheduled = false;
                _version++;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private int GetUpdateDelayMilliseconds()
        {
            if (_lastUpdateTimestamp <= 0)
                return 0;

            var elapsedMilliseconds = Stopwatch.GetElapsedTime(_lastUpdateTimestamp).TotalMilliseconds;
            return Math.Max(0, _throttleMilliseconds - (int)elapsedMilliseconds);
        }

        private void Timer_Tick(object state)
        {
            int version;
            lock (_sync)
                version = _version;

            BeginInvoke(version);
        }

        private void BeginInvoke(int version)
        {
            if (_disposed || _dispatcher.IsDisposed || !_dispatcher.IsHandleCreated)
                return;

            try
            {
                _dispatcher.BeginInvoke(new Action(() => ApplyPending(version)));
            }
            catch (ObjectDisposedException ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    "Failed to schedule a runtime focus update because the UI dispatcher was already disposed.",
                    ex);
            }
            catch (InvalidOperationException ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    "Failed to schedule a runtime focus update because the UI dispatcher was unavailable.",
                    ex);
            }
        }

        private void ApplyPending(int version)
        {
            BaseFlowBlock flowBlock;
            lock (_sync)
            {
                if (version != _version)
                {
                    _updateScheduled = false;
                    return;
                }

                flowBlock = _pendingFlowBlock;
                _updateScheduled = false;
                _lastUpdateTimestamp = Stopwatch.GetTimestamp();
            }

            _applyFocus(flowBlock);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ClearPending();
            _timer.Dispose();
        }
    }
}
