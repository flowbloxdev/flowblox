using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Events;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Util;
using System.Collections.ObjectModel;
using System.Data;

namespace FlowBlox.Core.Provider.Registry
{
    public class FlowBloxRegistry
    {
        private HashSet<BaseFlowBlock> _flowBlocks;
        private HashSet<IManagedObject> _managedObjects;
        private Dictionary<IFlowBloxComponent, IFlowBloxComponent> _originalReferences;
        private IDictionary<IFlowBloxComponent, IFlowBloxComponent> _originalReferencesReversed;

        public delegate void ManagedObjectRemovedHandler(ManagedObjectRemovedEventArgs eventArgs);
        public delegate void ManagedObjectAddedHandler(ManagedObjectAddedEventArgs eventArgs);
        public delegate void FlowBlockRemovedHandler(FlowBlockRemovedEventArgs eventArgs);
        public delegate void FlowBlockAddedHandler(FlowBlockAddedEventArgs eventArgs);

        public event ManagedObjectRemovedHandler OnManagedObjectRemoved;
        public event ManagedObjectAddedHandler OnManagedObjectAdded;
        public event FlowBlockRemovedHandler OnFlowBlockRemoved;
        public event FlowBlockAddedHandler OnFlowBlockAdded;

        public virtual bool SupportsDeletionStaging => false;

        public FlowBloxRegistry()
        {
            _flowBlocks = new HashSet<BaseFlowBlock>();
            _managedObjects = new HashSet<IManagedObject>();
            _originalReferences = new Dictionary<IFlowBloxComponent, IFlowBloxComponent>();
            _originalReferencesReversed = new Dictionary<IFlowBloxComponent, IFlowBloxComponent>();
        }

        public FlowBloxRegistry(FlowBloxRegistry copyFrom) : this()
        {
            if (copyFrom == null)
                return;

            _flowBlocks.AddRange(copyFrom.GetFlowBlocks());
            _managedObjects.AddRange(copyFrom.GetManagedObjects());
        }

        public List<BaseResultFlowBlock> GetResultFlowBlocks() => this.GetFlowBlocks().OfType<BaseResultFlowBlock>().ToList();
        
        public List<BaseFlowBlock> GetInputFlowBlocks()
        {
            return GetFlowBlocks()
                .Where(x => x.GetInputCardinality() != FlowBlockCardinalities.None)
                .ToList();
        }

        public List<FieldElement> GetRuntimeFields(bool orderedByExecutionFlow = false)
        {
            return GetFieldElements(orderedByExecutionFlow)
                .Where(x => !x.UserField)
                .ToList();
        }

        public List<FieldElement> GetUserFields(UserFieldTypes userFieldType = UserFieldTypes.None)
        {
            var userFields = GetFieldElements().Where(x => x.UserField);
            if (userFieldType == UserFieldTypes.None)
                return userFields.ToList();
            else
                return userFields.Where(x => x.UserFieldType == userFieldType).ToList();
        }

        private string GetNextName<T>(string prefix = null)
            where T : IFlowBloxComponent
        {
            return GetNextName(typeof(T), prefix);
        }

        private string GetNextName(Type type, string prefix = null)
        {
            prefix ??= type.Name;

            IEnumerable<IFlowBloxComponent> candidates;
            if (typeof(BaseFlowBlock).IsAssignableFrom(type))
            {
                candidates = GetFlowBlocks();
            }
            else if (typeof(IManagedObject).IsAssignableFrom(type))
            {
                candidates = GetManagedObjects();
            }
            else
            {
                throw new InvalidOperationException($"Cannot determine source collection for type {type.Name}.");
            }

            var existingNames = candidates
                .Where(obj => type == obj.GetType())
                .Select(obj => obj.Name)
                .Where(name => !string.IsNullOrEmpty(name) && name.StartsWith(prefix))
                .ToHashSet();

            for (int i = 0; i < int.MaxValue; i++)
            {
                string candidate = prefix + i;
                if (!existingNames.Contains(candidate))
                    return candidate;
            }

            throw new InvalidOperationException($"Unable to find a unique name with prefix '{prefix}' for type '{type.Name}'.");
        }

        private string GetNextName<T>(object prefix) 
            where T : IFlowBloxComponent
        {
            return GetNextName<T>(prefix?.ToString());
        }

        public FieldElement CreateUserField(UserFieldTypes userFieldType, FieldTypes fieldType = FieldTypes.Text, string fieldName = "")
        {
            int numericNameSuffix = GetUserFields().Count;

            var fieldElement = new FieldElement(fieldType)
            {
                UserField = true,
                UserFieldType = userFieldType,
                ListOfValues = new ObservableCollection<ValueItem>(),
                Name = !string.IsNullOrEmpty(fieldName) ?
                    fieldName :
                    GetNextName<FieldElement>(prefix: userFieldType)
            };

            fieldElement.OnAfterCreate();
            fieldElement.OnAfterLoad();

            _managedObjects.Add(fieldElement);

            return fieldElement;
        }

        public bool HasUserField(string fieldName)
        {
            string fullyQualifiedName = FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.User, fieldName);
            if (GetFieldElements().Any(x => x.FullyQualifiedName == fullyQualifiedName))
            {
                return true;
            }
            return false;
        }

        public void RemoveField(string fullyQualifiedFieldName)
        {
            foreach(var fieldElement in GetManagedObjects()
                .OfType<FieldElement>()
                .Where(x => x.FullyQualifiedName == fullyQualifiedFieldName))
            {
                RemoveManagedbject(fieldElement);
            }
        }

        public bool HasField(string fullyQualifiedFieldName)
        {
            return GetFieldElements().Any(x => x.FullyQualifiedName == fullyQualifiedFieldName);
        }

        public FieldElement GetFieldElementOrNull(string fullyQualifiedFieldName)
        {
            return GetFieldElements().FirstOrDefault(x => x.FullyQualifiedName == fullyQualifiedFieldName);
        }

        public FieldElement CreateField(
            BaseResultFlowBlock source, 
            FieldNameGenerationMode nameGenerationMode = FieldNameGenerationMode.UseFallbackIndexOnly, 
            FieldTypes fieldType = FieldTypes.Text)
        {
            var field = new FieldElement(source, nameGenerationMode, fieldType);
            field.OnAfterCreate();
            field.OnAfterLoad();
            _managedObjects.Add(field);
            OnManagedObjectAdded?.Invoke(new ManagedObjectAddedEventArgs(field));
            return field;
        }

        public BaseFlowBlock GetStartFlowBlock() => this.GetFlowBlocks().OfType<StartFlowBlock>().FirstOrDefault();

        public List<BaseFlowBlock> GetPreviousElements(BaseFlowBlock nextElement)
        {
            List<BaseFlowBlock> whereNextElementList = new List<BaseFlowBlock>();
            foreach (var element in this.GetFlowBlocks())
            {
                if (element.GetNextFlowBlocks().Contains(nextElement))
                    whereNextElementList.Add(element);
            }
            return whereNextElementList;
        }

        public IEnumerable<T> GetFlowBlocks<T>() where T : BaseFlowBlock => this.GetFlowBlocks().OfType<T>();

        public IEnumerable<BaseFlowBlock> GetFlowBlocks(Type type) => this.GetFlowBlocks().Where(x => type.IsAssignableFrom(x.GetType()));

        public virtual IEnumerable<BaseFlowBlock> GetFlowBlocks() => _flowBlocks;

        public virtual void StageDeletion(object target)
        {
            throw new NotSupportedException($"{GetType().Name} does not support deletion staging.");
        }

        public virtual IEnumerable<IManagedObject> GetManagedObjects() => _managedObjects;

        public virtual IEnumerable<T> GetManagedObjects<T>() where T : IManagedObject => GetManagedObjects().OfType<T>();

        public virtual IEnumerable<IManagedObject> GetManagedObjects(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!typeof(IManagedObject).IsAssignableFrom(type))
                throw new ArgumentException($"Type {type.FullName} does not implement IManagedObject.", nameof(type));

            return GetManagedObjects().Where(obj => type.IsInstanceOfType(obj));
        }

        public IEnumerable<BaseFlowBlock> GetFlowBlocksRecursiveOrderedByExecutionFlow(BaseFlowBlock baseFlowBlock)
        {
            if (baseFlowBlock == null)
                throw new ArgumentNullException(nameof(baseFlowBlock));

            return GetFlowBlocksRecursiveOrderedByExecutionFlow(baseFlowBlock, new HashSet<BaseFlowBlock>());
        }

        private IEnumerable<BaseFlowBlock> GetFlowBlocksRecursiveOrderedByExecutionFlow(
            BaseFlowBlock baseFlowBlock,
            HashSet<BaseFlowBlock> visited)
        {
            if (baseFlowBlock == null)
                throw new ArgumentNullException(nameof(baseFlowBlock));

            if (!visited.Add(baseFlowBlock))
                yield break;

            yield return baseFlowBlock;

            foreach (var nextFlowBlock in baseFlowBlock.GetNextFlowBlocks())
            {
                if (nextFlowBlock.ReferencedFlowBlocks.Count == 1)
                {
                    foreach (var flowBlock in GetFlowBlocksRecursiveOrderedByExecutionFlow(nextFlowBlock, visited))
                        yield return flowBlock;
                }

                if (nextFlowBlock.ReferencedFlowBlocks.Count > 1 &&
                    nextFlowBlock.ReferencedFlowBlocks.All(x => visited.Contains(x)))
                {
                    foreach (var flowBlock in GetFlowBlocksRecursiveOrderedByExecutionFlow(nextFlowBlock, visited))
                        yield return flowBlock;
                }
            }
        }

        private IEnumerable<FieldElement> GetFieldElementsRecursiveOrderedByExecutionFlow(BaseFlowBlock baseFlowBlock)
        {
            HashSet<BaseFlowBlock> visited = new HashSet<BaseFlowBlock>();
            return GetFieldElementsRecursiveOrderedByExecutionFlow(baseFlowBlock, visited);
        }

        private IEnumerable<FieldElement> GetFieldElementsRecursiveOrderedByExecutionFlow(BaseFlowBlock baseFlowBlock, HashSet<BaseFlowBlock> visited)
        {
            visited.AddIfNotExists(baseFlowBlock);

            if (baseFlowBlock is BaseResultFlowBlock resultFlowBlock)
            {
                foreach (var field in resultFlowBlock.Fields)
                    yield return field;
            }

            foreach (var nextFlowBlock in baseFlowBlock.GetNextFlowBlocks())
            {
                if (nextFlowBlock.ReferencedFlowBlocks.Count == 1)
                {
                    foreach (var fieldElement in GetFieldElementsRecursiveOrderedByExecutionFlow(nextFlowBlock))
                        yield return fieldElement;
                }

                if (nextFlowBlock.ReferencedFlowBlocks.Count > 1 && 
                    nextFlowBlock.ReferencedFlowBlocks.All(x => visited.TryGetValue(x, out _)))
                {
                    foreach (var fieldElement in GetFieldElementsRecursiveOrderedByExecutionFlow(nextFlowBlock))
                        yield return fieldElement;
                }
            }
        }

        public IEnumerable<FieldElement> GetFieldElements(bool orderedByExecutionFlow = false)
        {
            var startFlowBlock = GetStartFlowBlock();
            if (orderedByExecutionFlow && startFlowBlock != null)
            {
                var result = GetFieldElementsRecursiveOrderedByExecutionFlow(startFlowBlock);
                return result.Concat(GetManagedObjects().OfType<FieldElement>().Except(result));
            }
            else
            {
                return GetManagedObjects().OfType<FieldElement>();
            }
        }

        public virtual void RegisterFlowBlock(BaseFlowBlock flowBlock)  
        {
            if (_flowBlocks.Contains(flowBlock))
                return;

            _flowBlocks.Add(flowBlock);
            OnFlowBlockAdded?.Invoke(new FlowBlockAddedEventArgs(flowBlock));

            // Register fields
            if (flowBlock is BaseResultFlowBlock)
            {
                var resultFlowBlock = (BaseResultFlowBlock)flowBlock;
                resultFlowBlock.Fields.ForEach(y => y.Source = resultFlowBlock);
                resultFlowBlock.Fields.ForEach(y => this.Register(y));
            }

            // Register managed objects (transitional)
            foreach (var pi in flowBlock.GetType().GetProperties()
                .Where(x => typeof(IManagedObject).IsAssignableFrom(x.PropertyType) || 
                            (x.PropertyType.IsGenericType && typeof(IEnumerable<IManagedObject>).IsAssignableFrom(x.PropertyType))))
            {
                var propertyValue = pi.GetValue(flowBlock);
                if (propertyValue == null)
                    continue;

                if (propertyValue is IManagedObject managedObject)
                {
                    this.Register(managedObject);
                }
                else if (propertyValue is IEnumerable<IManagedObject> managedObjects)
                {
                    foreach (var obj in managedObjects)
                    {
                        this.Register(obj);
                    }
                }
            }
        }

        public virtual void RegisterManagedObject(IManagedObject managedObject)
        {
            if (!_managedObjects.Contains(managedObject))
            {
                _managedObjects.Add(managedObject);
                OnManagedObjectAdded?.Invoke(new ManagedObjectAddedEventArgs(managedObject));
            }
                
        }

        public virtual void RemoveFlowBlock(BaseFlowBlock flowBlock)
        {
            if (!_flowBlocks.Remove(flowBlock))
                return;

            OnFlowBlockRemoved?.Invoke(new FlowBlockRemovedEventArgs(flowBlock));

            foreach(var managedObject in flowBlock.DefinedManagedObjects)
            {
                RemoveManagedbject(managedObject);
            }
        }

        public virtual void RemoveManagedbject(IManagedObject managedObject)
        {
            _managedObjects.Remove(managedObject);
            OnManagedObjectRemoved?.Invoke(new ManagedObjectRemovedEventArgs(managedObject));
        }

        public virtual void Register(object item)
        {
            if (item is BaseFlowBlock)
                RegisterFlowBlock((BaseFlowBlock)item);
            if (item is IManagedObject)
                RegisterManagedObject((IManagedObject)item);
        }

        public virtual void Unregister(object item)
        {
            if (item is BaseFlowBlock)
                RemoveFlowBlock((BaseFlowBlock)item);
            if (item is IManagedObject)
                RemoveManagedbject((IManagedObject)item);
        }

        public virtual void ReplaceManagedObjectCollection<T>(List<T> managedObjects) where T : IManagedObject
        {
            foreach(var toRemove in _managedObjects.OfType<T>())
                _managedObjects.Remove(toRemove);

            foreach (var toAdd in managedObjects)
                _managedObjects.Add(toAdd);
        }

        public virtual void ReplaceRef(BaseFlowBlock from, BaseFlowBlock to)
        {
            StoreOriginalRef(from, to);

            if (_flowBlocks.Contains(from))
            {
                _flowBlocks.Remove(from);
                _flowBlocks.Add(to);
            }
        }

        public virtual void ReplaceRef(IManagedObject from, IManagedObject to)
        {
            StoreOriginalRef(from, to);

            if (_managedObjects.Contains(from))
            {
                _managedObjects.Remove(from);
                _managedObjects.Add(to);
            }
        }

        private void StoreOriginalRef(IFlowBloxComponent from, IFlowBloxComponent to)
        {
            if (!_originalReferences.ContainsKey(from))
                _originalReferences[to] = from;
            else
                _originalReferences[to] = _originalReferences[from];

            if (_originalReferences.TryReverseDictionary(out IDictionary<IFlowBloxComponent, IFlowBloxComponent> reversed))
                _originalReferencesReversed = reversed;
        }

        public IFlowBloxComponent GetOriginalRef(IFlowBloxComponent flowBloxComponent)
        {
            return _originalReferences.TryGetValue(flowBloxComponent, out var originalReference)
                ? originalReference
                : flowBloxComponent;
        }

        /// <summary>
        /// Reloads the given FlowBlox components within the current transaction context,
        /// returning the copied references that correspond to the original references whenever available.
        /// </summary>
        /// <typeparam name="T">FlowBlox component type.</typeparam>
        /// <param name="flowBloxComponents">The components to reload.</param>
        /// <returns>An enumerable of reloaded components casted to <typeparamref name="T"/>.</returns>
        public IEnumerable<T> Reload<T>(IEnumerable<T> flowBloxComponents)
            where T : class, IFlowBloxComponent
        {
            if (flowBloxComponents == null)
                return Enumerable.Empty<T>();

            return flowBloxComponents.Select(Reload);
        }

        /// <summary>
        /// Reloads a single FlowBlox component within the current transaction context,
        /// returning the copied reference that corresponds to the original reference whenever available.
        /// </summary>
        /// <typeparam name="T">FlowBlox component type.</typeparam>
        /// <param name="flowBloxComponent">The component to reload.</param>
        /// <returns>
        /// The reloaded component casted to <typeparamref name="T"/>.
        /// </returns>
        public T Reload<T>(T flowBloxComponent)
            where T : class, IFlowBloxComponent
        {
            if (flowBloxComponent == null)
                return null;

            return _originalReferencesReversed.TryGetValue(flowBloxComponent, out var copiedReference) ? 
                (T)copiedReference : 
                flowBloxComponent;
        }

        public T CreateFlowBlockUnregistered<T>() where T : BaseFlowBlock
        {
            return (T)CreateFlowBlockUnregistered(typeof(T));
        }

        public BaseFlowBlock CreateFlowBlockUnregistered(Type type)
        {
            var flowBlock  = (BaseFlowBlock)Activator.CreateInstance(type);
            flowBlock!.Name = flowBlock.NamePrefix;
            if (flowBlock.CreateNumericNameSuffix)
                flowBlock.Name = GetNextName(type, flowBlock.NamePrefix);
            return flowBlock;
        }

        public void PostProcessManagedObjectCreated(IManagedObject managedObject)
        {
            if (string.IsNullOrEmpty(managedObject.Name))
                managedObject.Name = GetNextName(managedObject.GetType());

            managedObject.OnAfterCreate();
            managedObject.OnAfterLoad();
        }

        public void PostProcessFlowBlockCreated(BaseFlowBlock flowBlock)
        {
            flowBlock.OnAfterCreate();
            flowBlock.OnAfterLoad();
        }
    }
}
