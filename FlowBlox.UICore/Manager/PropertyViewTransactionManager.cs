using FlowBlox.Core.Constants;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.DependencyInjection;
using System.IO;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Actions;
using FlowBlox.UICore.Interfaces;

namespace FlowBlox.UICore.Manager
{
    public class PropertyViewTransactionManager
	{
		private DynamicDeepCopier _deepCopier;
		private FlowBloxRegistry _registry;
		private FlowBloxRegistry _transactionRegistry;
		private Dictionary<object, object> _refMappings = new Dictionary<object, object>();
        private IFlowBloxProjectComponentProvider _componentProvider;
        private string _transactionProtocolFilePath;
        private bool _detached;

        public PropertyViewTransactionManager()
		{
			_deepCopier = new DynamicDeepCopier();
			_deepCopier.OnAfterCopy += _deepCopier_OnAfterCopy;
			_registry = FlowBloxRegistryProvider.GetRegistry();
            _componentProvider = FlowBloxServiceLocator.Instance.GetService<IFlowBloxProjectComponentProvider>();
        }

		private void _deepCopier_OnAfterCopy(object source, object copy)
		{
			if (source is IManagedObject && copy is IManagedObject)
			{
				_refMappings[source] = copy;
				_registry.ReplaceRef((IManagedObject)source, (IManagedObject)copy);
			}

			if (source is BaseFlowBlock && copy is BaseFlowBlock)
			{
				_refMappings[source] = copy;
				_registry.ReplaceRef((BaseFlowBlock)source, (BaseFlowBlock)copy);
			}

            if (copy is IFlowBloxComponent component)
            {
                component.OnAfterLoad();
            }
            else if (copy is FlowBloxReactiveObject reactiveObject &&
                     FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(reactiveObject))
            {
                reactiveObject.OnAfterLoad();
            }
		}

		public class OpenResult
		{
			public object TransientTarget { get; set; }
			public FlowBloxRegistry Registry { get; set; }
		}

		public OpenResult Open(object target, bool detached = false, bool nested = false)
		{
			if (_transactionRegistry != null)
				throw new InvalidOperationException("A transaction is already active.");

			_detached = detached;
			_transactionRegistry = FlowBloxRegistryProvider.OpenTransaction(detached, target, nested);
			_registry = _transactionRegistry;
			_deepCopier.PropertyActions = FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(target);
			var transientTarget = _deepCopier.Copy(target);
			var protocol = _deepCopier.GetProtocol();
			StoreProtocol(target, protocol);

			return new OpenResult
			{
				TransientTarget = transientTarget,
				Registry = _registry
			};
		}

        /// <summary>
        /// Adds a new object to the transaction by converting it into a transient object.
        /// </summary>
        /// <param name="sourceObject"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public object Append(object sourceObject)
        {
            if (sourceObject == null)
                throw new ArgumentNullException(nameof(sourceObject));

            var copiedObject = _deepCopier.Copy(sourceObject);
            var protocol = _deepCopier.GetProtocol();

			if (!string.IsNullOrWhiteSpace(_transactionProtocolFilePath))
				StoreProtocol(protocol, _transactionProtocolFilePath);

            return copiedObject;
        }

        public void Cancel()
		{
			if (_transactionRegistry == null)
				return;

            FlowBloxPendingActionStore.Take(_transactionRegistry);
			FlowBloxRegistryProvider.CancelTransaction(_transactionRegistry);
			_transactionRegistry = null;
		}

		public void Commit(object target, object transientTarget)
		{
			if (!FlowBloxRegistryProvider.IsCurrentTransaction(_transactionRegistry))
				throw new InvalidOperationException("The transaction is not the current registry transaction and cannot be committed.");

			// Create restore object
			var editAction = new FlowBloxEditAction { Target = target };
			editAction.CaptureRestoreState();

			// Copy changes to the target object
			_deepCopier.PropertyActions = FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(transientTarget);
			_deepCopier.SpecialMode = DeepCopierSpecialMode.Recopy;
			_deepCopier.Copy(transientTarget, target);
			var protocol = _deepCopier.GetProtocol();
			StoreProtocol(transientTarget, protocol, true);

			// Create repetition object
			editAction.CaptureRepetitionState();

            var pendingActions = FlowBloxPendingActionStore.Take(_transactionRegistry);
            var invokedActions = new List<FlowBloxBaseAction>();
            try
            {
                foreach (var action in pendingActions)
                {
                    action.Invoke();
                    invokedActions.Add(action);
                }
            }
            catch
            {
                foreach (var action in invokedActions.AsEnumerable().Reverse())
                    action.Undo();
                editAction.Undo();
                throw;
            }

            foreach (var action in pendingActions)
				editAction.AssociatedActions.Add(action);

			// Create and append change
			CreateAndAppendChange(editAction);

            // Committing the changes to the registry
            FlowBloxRegistryProvider.CommitTransaction(_transactionRegistry);
			_transactionRegistry = null;
		}

		private void CreateAndAppendChange(FlowBloxEditAction editAction)
		{
			if (_detached)
				return;

            _componentProvider.GetCurrentChangelist()?.AddChange(editAction);
        }

		private void StoreProtocol(string protocol, string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath))
				throw new ArgumentException("Property name must not be null, empty, or whitespace.", nameof(filePath));

            File.WriteAllText(filePath, protocol);
        }

		private void StoreProtocol(object target, string protocol, bool recopy = false)
		{
			var protocolDirectory = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsDeepCopierProtocolDir).Value;

			if (!Directory.Exists(protocolDirectory))
				Directory.CreateDirectory(protocolDirectory);

			string fileName = target.GetType().Name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (recopy ? "_recopy" : "") + ".txt";
            _transactionProtocolFilePath = protocolDirectory + "\\" + fileName;
			StoreProtocol(protocol, _transactionProtocolFilePath);
        }
	}
}
