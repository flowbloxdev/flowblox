using FlowBlox.Core.Enums;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Notifications;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Runtime.Debugging;
using FlowBlox.Core.Services.Notifications;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FlowBlox.Core.Interceptors
{
    public sealed class RuntimeNotificationInterceptor : RuntimeInterceptorBase
    {
        private readonly IReadOnlyCollection<IRuntimeNotificationProvider> _providers;
        private RuntimeNotificationConfiguration _configuration;
        private RuntimeNotificationContext _lastError;
        private RuntimeNotificationContext _abortTriggeringError;
        private RuntimeCancellationContext _cancellation;
        private Exception _unexpectedException;
        private bool _runtimeStarted;
        private bool _finished;

        public RuntimeNotificationInterceptor(IEnumerable<IRuntimeNotificationProvider> providers)
        {
            _providers = providers?.ToList() ?? new List<IRuntimeNotificationProvider>();
        }

        public override void NotifyBeforeRuntimeStarted()
        {
            if (Runtime?.EnableAppNotifications == false)
            {
                _configuration = null;
                return;
            }

            _configuration = RuntimeNotificationConfigurationStore.Load(out var error);
            if (!string.IsNullOrWhiteSpace(error))
                FlowBloxLogManager.Instance.GetLogger().Warn(error);
        }

        public override void NotifyRuntimeStarted()
        {
            _runtimeStarted = true;
            Send(RuntimeNotificationType.RuntimeStartedSuccessfully, "Runtime execution has started.");
        }

        public override void NotifyError(BaseFlowBlock flowBlock, string message, Exception exception = null)
        {
            if (_configuration == null)
                return;

            _lastError = CreateContext(RuntimeNotificationType.RuntimeError, message, exception, flowBlock?.Name);
            if (_cancellation?.CancellationKind == RuntimeCancellationKind.AbortOnWarningOrError)
                _abortTriggeringError = _lastError;
            Send(_lastError);
        }

        public override void NotifyRuntimeCancelled(RuntimeCancellationContext cancellationContext)
        {
            _cancellation = cancellationContext;
        }

        public override void NotifyRuntimeAborted(Exception exception)
        {
            _unexpectedException = exception;
            if (!_runtimeStarted)
                Send(RuntimeNotificationType.RuntimeStartFailed, exception?.Message, exception);
        }

        public override void NotifyRuntimeFinished()
        {
            if (_configuration == null || _finished)
                return;

            _finished = true;
            var wasAborted = Runtime?.Aborted == true || _cancellation != null || _unexpectedException != null;
            if (wasAborted)
            {
                // A failure before RuntimeStarted is represented by the dedicated start-failure
                // state sent from NotifyRuntimeAborted, not by a second abort notification.
                if (!_runtimeStarted && _unexpectedException != null)
                    return;

                var exception = _unexpectedException ?? _abortTriggeringError?.Exception;
                var message = _cancellation?.Reason;
                if (string.IsNullOrWhiteSpace(message))
                    message = _abortTriggeringError?.Message ?? _unexpectedException?.Message ?? "Runtime execution was aborted.";

                var context = CreateContext(
                    RuntimeNotificationType.RuntimeAborted,
                    message,
                    exception,
                    _abortTriggeringError?.FlowBlockName);
                context.TriggeringErrorMessage = _abortTriggeringError?.Message ?? string.Empty;
                context.Cancellation = _cancellation ?? Runtime?.CancellationContext;
                Send(context);
                return;
            }

            if (_lastError == null)
                Send(RuntimeNotificationType.RuntimeCompletedSuccessfully, "Runtime execution completed successfully.");
        }

        private void Send(RuntimeNotificationType type, string message, Exception exception = null)
        {
            if (_configuration?.IsEnabled(type) == true)
                Send(CreateContext(type, message, exception));
        }

        private void Send(RuntimeNotificationContext context)
        {
            if (_configuration == null || !_configuration.IsEnabled(context.Type))
                return;
            if (_configuration.DoNotSendWhileDebugging && context.IsDebugging)
                return;

            foreach (var provider in _providers)
            {
                try
                {
                    provider.Send(_configuration, context);
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        $"Runtime notification provider '{provider.Name}' failed.", ex);
                }
            }
        }

        private RuntimeNotificationContext CreateContext(
            RuntimeNotificationType type,
            string message,
            Exception exception = null,
            string flowBlockName = null)
        {
            var process = Process.GetCurrentProcess();
            var hostName = GetHostName();
            return new RuntimeNotificationContext
            {
                Type = type,
                OccurredAt = DateTime.Now,
                RuntimeStartedAt = Runtime?.Started ?? default,
                ProjectName = Project?.ProjectName ?? string.Empty,
                Message = message ?? string.Empty,
                Exception = exception,
                FlowBlockName = flowBlockName ?? string.Empty,
                HostName = hostName,
                IpAddresses = GetIpAddresses(hostName),
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                RuntimeLogFilePath = (Runtime as FlowBloxRuntime)?.GetLogfilePath() ?? string.Empty,
                Cancellation = Runtime?.CancellationContext,
                IsDebugging = IsDebuggingRuntime()
            };
        }

        private bool IsDebuggingRuntime() =>
            Runtime?.ExternalDebuggingInformation != null ||
            Runtime is FlowBloxRuntime runtime && !runtime.IsNoDesignerMode;

        private static string GetHostName()
        {
            try { return Dns.GetHostName(); }
            catch { return Environment.MachineName; }
        }

        private static IReadOnlyCollection<string> GetIpAddresses(string hostName)
        {
            try
            {
                return Dns.GetHostAddresses(hostName)
                    .Where(x => x.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .OrderBy(x => x.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .Select(x => x.ToString())
                    .Distinct()
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
