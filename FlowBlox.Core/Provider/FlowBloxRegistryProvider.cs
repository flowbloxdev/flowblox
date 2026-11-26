using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Provider.Registry;

namespace FlowBlox.Core.Provider
{
    public static class FlowBloxRegistryProvider
    {
        private static readonly object _registryChainSync = new();
        private static readonly List<FlowBloxRegistry> _registryChain = new();
        private static readonly HashSet<FlowBloxRegistry> _nestedTransactionRegistries = new();
        private static readonly Dictionary<FlowBloxRegistry, object> _transactionTargets = new();
        private static readonly AsyncLocal<FlowBloxRegistry> _scopedRegistry = new();

        public static event EventHandler TransactionsChanged;

        public static bool IsCurrentlyDetached
        {
            get
            {
                lock (_registryChainSync)
                    return _registryChain.Any(x => x is FlowBloxDetachedRegistry);
            }
        }

        public static FlowBloxRegistry GetRegistry()
        {
            var scopedRegistry = _scopedRegistry.Value;
            if (scopedRegistry != null)
                return scopedRegistry;

            lock (_registryChainSync)
            {
                if (_registryChain.Any())
                    return _registryChain.Last();
            }

            var registry = ThreadBasedGridElementRegistryProvider.GetManagedObject();
            if (registry == null)
            {
                var project = FlowBloxProjectManager.Instance.ActiveProject;
                registry = project?.FlowBloxRegistry;
            }

            return registry;
        }

        [Obsolete("This is only for FlowBlox internal use. Use GetRegistry instead.", false)]
        public static FlowBloxRegistry GetProjectRegistry()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            return project?.FlowBloxRegistry;
        }

        public static IDisposable BeginScopedRegistry(FlowBloxRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);

            var previousRegistry = _scopedRegistry.Value;
            _scopedRegistry.Value = registry;
            return new ScopedRegistryScope(previousRegistry);
        }

        public static FlowBloxRegistry OpenTransaction(
            bool detached = false,
            object target = null,
            bool nested = false)
        {
            FlowBloxRegistry transactionRegistry;
            int depth;
            lock (_registryChainSync)
            {
                var currentTransaction = _registryChain.LastOrDefault();
                if (nested &&
                    currentTransaction != null &&
                    _nestedTransactionRegistries.Contains(currentTransaction))
                {
                    throw new InvalidOperationException(
                        "A nested transaction cannot be opened on top of another nested transaction.");
                }

                var parentRegistry = _registryChain.LastOrDefault() ?? GetRegistry();
                transactionRegistry = detached
                    ? new FlowBloxDetachedRegistry(parentRegistry)
                    : new FlowBloxTransientRegistry(parentRegistry);

                _registryChain.Add(transactionRegistry);
                _transactionTargets[transactionRegistry] = target;
                if (nested)
                    _nestedTransactionRegistries.Add(transactionRegistry);
                depth = _registryChain.Count;
            }

            FlowBloxRegistryTransactionHistory.RegisterOpened(
                transactionRegistry,
                target,
                detached,
                nested,
                depth);
            OnTransactionsChanged();
            return transactionRegistry;
        }

        public static bool IsCurrentTransactionNested()
        {
            lock (_registryChainSync)
            {
                return _registryChain.Any() &&
                       _nestedTransactionRegistries.Contains(_registryChain.Last());
            }
        }

        public static bool IsCurrentNestedTransactionForDifferentTarget(object target)
        {
            lock (_registryChainSync)
            {
                var currentTransaction = _registryChain.LastOrDefault();
                if (currentTransaction == null ||
                    !_nestedTransactionRegistries.Contains(currentTransaction))
                    return false;

                if (target == null ||
                    !_transactionTargets.TryGetValue(currentTransaction, out var transactionTarget))
                    return true;

                var resolvedTarget = ResolveTransactionTarget(currentTransaction, target);
                var resolvedTransactionTarget = ResolveTransactionTarget(currentTransaction, transactionTarget);
                return !ReferenceEquals(resolvedTarget, resolvedTransactionTarget);
            }
        }

        public static void CommitTransaction()
            => CommitTransaction(GetCurrentTransaction());

        public static void CommitTransaction(FlowBloxRegistry transactionRegistry)
        {
            EnsureCurrentTransaction(transactionRegistry);

            if (transactionRegistry is FlowBloxTransientRegistry transientRegistry)
                transientRegistry.Commit();

            CompleteTransaction(transactionRegistry, FlowBloxRegistryTransactionState.Committed);
        }

        public static void CancelTransaction() => CancelTransaction(GetCurrentTransaction());

        public static void CancelTransaction(FlowBloxRegistry transactionRegistry)
        {
            EnsureCurrentTransaction(transactionRegistry);
            CompleteTransaction(transactionRegistry, FlowBloxRegistryTransactionState.Cancelled);
        }

        /// <summary>
        /// Checks whether a bound transaction is still open, including while another transaction is above it.
        /// </summary>
        public static bool IsTransactionActive(FlowBloxRegistry transactionRegistry)
        {
            lock (_registryChainSync)
            {
                return transactionRegistry != null && _registryChain.Contains(transactionRegistry);
            }
        }

        public static bool IsCurrentTransaction(FlowBloxRegistry transactionRegistry)
        {
            lock (_registryChainSync)
            {
                return transactionRegistry != null &&
                       _registryChain.Any() &&
                       ReferenceEquals(_registryChain.Last(), transactionRegistry);
            }
        }

        private static FlowBloxRegistry GetCurrentTransaction()
        {
            lock (_registryChainSync)
            {
                if (!_registryChain.Any())
                    throw new InvalidOperationException("No registry transaction is active.");

                return _registryChain.Last();
            }
        }

        private static void EnsureCurrentTransaction(FlowBloxRegistry transactionRegistry)
        {
            if (transactionRegistry == null)
                throw new ArgumentNullException(nameof(transactionRegistry));

            if (!IsCurrentTransaction(transactionRegistry))
                throw new InvalidOperationException("The specified registry transaction is not the current transaction.");
        }

        private static void CompleteTransaction(
            FlowBloxRegistry registry,
            FlowBloxRegistryTransactionState state)
        {
            lock (_registryChainSync)
            {
                if (!_registryChain.Remove(registry))
                    return;

                _nestedTransactionRegistries.Remove(registry);
                _transactionTargets.Remove(registry);
            }

            FlowBloxRegistryTransactionHistory.RegisterCompleted(registry, state);
            OnTransactionsChanged();
        }

        private static void OnTransactionsChanged() =>
            TransactionsChanged?.Invoke(null, EventArgs.Empty);

        private static object ResolveTransactionTarget(FlowBloxRegistry registry, object target)
        {
            if (target is IFlowBloxComponent component)
                return registry.GetOriginalRef(component);

            return target;
        }

        private sealed class ScopedRegistryScope : IDisposable
        {
            private readonly FlowBloxRegistry _previousRegistry;
            private bool _disposed;

            public ScopedRegistryScope(FlowBloxRegistry previousRegistry)
            {
                _previousRegistry = previousRegistry;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _scopedRegistry.Value = _previousRegistry;
            }
        }
    }
}
