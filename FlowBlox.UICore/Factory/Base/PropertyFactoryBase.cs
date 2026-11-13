using FlowBlox.Core.Extensions;
using PropertyViewControlFactoryBaseResources = FlowBlox.UICore.Resources.ClassResources.PropertyViewControlFactoryBase;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Actions;
using FlowBlox.UICore.Util;
using System.Reflection;

namespace FlowBlox.UICore.Factory.Base
{
    public abstract class PropertyViewControlFactoryBase<TOwner>
    {
        protected readonly object _target;
        protected readonly PropertyInfo _property;
        protected readonly FlowBloxUIAttribute _uiAttribute;
        protected readonly FlowBloxAssociationAttribute _associationAttribute;
        protected readonly FlowBloxItemsSourceAttribute _itemsSourceAttribute;
        protected readonly FlowBloxRegistry _registry;
        protected readonly bool _readOnly;
        private FlowBloxBaseAction _pendingDeletionAction;
        private object _pendingDeletionTarget;

        protected PropertyViewControlFactoryBase(PropertyInfo property, object target, bool readOnly)
        {
            _property = property;
            _uiAttribute = property.GetFlowBloxUIAttribute();
            _associationAttribute = property.GetFlowBloxAssociationAttribute();
            _itemsSourceAttribute = property.GetFlowBloxItemsSourceAttribute();
            _target = target;
            _registry = FlowBloxRegistryProvider.GetRegistry();
            _readOnly = readOnly;
        }

        protected object CreateNewInstance(TOwner owner) => CreateNewInstance(owner, _property.PropertyType);

        protected object CreateNewInstance(TOwner owner, Type propertyType)
        {
            IList<Type> types;
            if (propertyType.IsInterface || propertyType.IsAbstract)
            {
                if (_associationAttribute?.CreatableTypes != null && _associationAttribute.CreatableTypes.Length > 0)
                {
                    types = FlowBloxSupportedTypesResolver.ResolveSupportedTypes(
                        _target,
                        propertyType,
                        _associationAttribute.CreatableTypes);
                }
                else
                {
                    types = FlowBloxSupportedTypesResolver.ResolveSupportedTypes(_target, propertyType);
                }
            }
            else
            {
                types = new List<Type> { propertyType };
            }

            return CreateNewInstance(owner, propertyType, types);
        }

        protected object CreateNewInstance(TOwner owner, Type propertyType, IList<Type> types)
        {
            object newInstance;
            if (types.Count > 1)
            {
                var selectedType = ShowTypeSelectionDialog(owner, types);
                if (selectedType != null)
                    newInstance = CreateNewInstance(selectedType);
                else
                    return null;
            }
            else if (types.Count == 1)
            {
                newInstance = CreateNewInstance(types.Single());
            }
            else
            {
                throw new InvalidOperationException("No createable type could be determined.");
            }

            if (newInstance is IManagedObject managedObject)
            {
                _registry.PostProcessManagedObjectCreated(managedObject);
            }

            if (newInstance is FlowBloxReactiveObject reactiveObject &&
                FlowBloxReactiveObjectHelper.IsReactiveObjectOnly(reactiveObject))
            {
                reactiveObject.OnAfterLoad();
            }

            _registry.Register(newInstance);

            return newInstance;
        }

        protected virtual object CreateNewInstance(Type type)
        {
            if (TryCreateInstanceWithItemFactory(type, out var factoryInstance))
                return factoryInstance;

            ConstructorInfo constructor = type.GetConstructor([_target.GetType()]);
            object instance;
            if (constructor != null)
            {
                instance = constructor.Invoke([_target]);
            }
            else
            {
                constructor = type.GetConstructor(Type.EmptyTypes);
                if (constructor != null)
                    instance = constructor.Invoke(null);
                else
                    throw new InvalidOperationException("No suitable constructor found for type " + type.FullName);
            }

            return instance;
        }

        protected virtual Type GetExplicitItemFactoryType(Type type) => null;

        private bool TryCreateInstanceWithItemFactory(Type type, out object instance)
        {
            instance = null;
            var factoryType = GetExplicitItemFactoryType(type);
            if (factoryType == null)
                return false;

            var factory = (IItemFactory<object>)Activator.CreateInstance(factoryType);
            instance = factory.Create();
            if (instance == null || !type.IsInstanceOfType(instance))
                throw new InvalidOperationException($"Factory returned an incompatible instance. Expected type: {type.FullName}, actual: {instance?.GetType().FullName ?? "null"}");

            return true;
        }

        protected virtual void DeleteInstance(object instance)
        {
            if (ReferenceEquals(instance, _pendingDeletionTarget) && _pendingDeletionAction != null)
            {
                if (_registry.SupportsDeletionStaging &&
                    FlowBloxRegistryProvider.IsCurrentTransaction(_registry))
                {
                    FlowBloxPendingActionStore.Add(_registry, _pendingDeletionAction);
                }
                else
                {
                    _pendingDeletionAction.Invoke();
                }
                _pendingDeletionAction = null;
                _pendingDeletionTarget = null;
                return;
            }

            FlowBloxDeletionHelper.Delete(_registry, instance);
        }

        public async Task<bool> ConfirmDeletionAsync(object instance, TOwner owner, object excludeTarget = null)
        {
            var excludedDependencyContexts = new[] { _target, excludeTarget }
                .Where(x => x != null);
            var report = FlowBloxDeletionHelper.Analyze(
                _registry,
                new[] { instance },
                excludedDependencyContexts);
            if (owner is not System.Windows.Window window ||
                !DeletionConfirmationService.Confirm(window, report))
                return false;

            _pendingDeletionTarget = instance;
            _pendingDeletionAction = FlowBloxDeletionHelper.CreateAction(_registry, report);
            return true;
        }

        public MethodInfo GetSelectionFilterMethod(object target, string methodName, Type listType = null)
        {
            if (string.IsNullOrEmpty(methodName))
                throw new ArgumentNullException(nameof(methodName), "No selection method was passed.");

            MethodInfo filterMethod;

            // Check instance methods first
            var targetType = target.GetType();
            filterMethod = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (filterMethod != null)
                return filterMethod;

            // Check static methods next
            filterMethod = listType?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            return filterMethod;
        }

        public virtual void Reload()
        {
        }

        protected abstract Type ShowTypeSelectionDialog(TOwner owner, IList<Type> types);

        protected abstract Task ShowDependencyViolationDialogAsync(TOwner owner, List<string> allReferences, List<IFlowBloxComponent> dependencies);
    }
}
