using System.Collections;
using System.Reflection;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Provider.Registry;
using FlowBlox.Core.Util.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Models
{
    /// <summary>
    /// Binds a component conversation to one editor working copy and its transaction.
    /// </summary>
    public sealed class AssistantComponentContext
    {
        private readonly HashSet<object> _ownedObjects = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<IManagedObject> _createdObjects = new(ReferenceEqualityComparer.Instance);
        private readonly Func<bool> _isCurrent;
        private readonly Func<object, object> _appendWorkingCopy;

        public FlowBloxComponent Target { get; }
        public FlowBloxRegistry Registry { get; }
        public string HistoryScope { get; }
        public bool IsClosed { get; set; }
        public Func<Func<Task<ToolResponse>>, Task<ToolResponse>>? DispatchAsync { get; set; }
        public Func<Task>? ChangedAsync { get; set; }

        public AssistantComponentContext(
            FlowBloxComponent target,
            FlowBloxRegistry registry,
            Func<bool> isCurrent,
            Func<object, object> appendWorkingCopy)
        {
            Target = target;
            Registry = registry;
            _isCurrent = isCurrent;
            _appendWorkingCopy = appendWorkingCopy;
            HistoryScope = target is FieldElement field ? field.FullyQualifiedName : target.Name;
            _ownedObjects.Add(target);
            if (target is BaseFlowBlock flowBlock)
            {
                foreach (var item in registry.GetManagedObjects().Where(flowBlock.HasBackReference))
                    _ownedObjects.Add(item);
            }
        }

        public void EnsureActive()
        {
            if (IsClosed || !_isCurrent())
                throw new InvalidOperationException("The component editor transaction is no longer current. Reopen the component assistant.");
        }

        public void EnsureWritable(object target)
        {
            EnsureActive();
            var flowBlock = Target as BaseFlowBlock;
            var owned = target as IManagedObject;
            if (!_ownedObjects.Contains(target) ||
                (!ReferenceEquals(target, Target) && !_createdObjects.Contains(owned!) &&
                    (flowBlock == null || owned == null || !flowBlock.HasBackReference(owned))))
                throw new InvalidOperationException("This component is outside the editor scope. Only the current component and its owned back references can be edited.");
        }

        public string CaptureJson()
        {
            var settings = JsonSettings.ComponentSnapshotExport();
            var flowBlock = Target as BaseFlowBlock;
            return new JObject
            {
                ["component"] = JToken.Parse(JsonConvert.SerializeObject(Target, settings)),
                ["ownedObjects"] = new JArray(_ownedObjects.OfType<IManagedObject>()
                    .Where(item => !ReferenceEquals(item, Target) && (flowBlock?.HasBackReference(item) == true || _createdObjects.Contains(item)))
                    .Select(item => JToken.Parse(JsonConvert.SerializeObject(item, settings))))
            }.ToString(Formatting.None);
        }

        public string Describe()
        {
            return new JObject
            {
                ["mode"] = "component",
                ["target"] = Target is FieldElement field ? field.FullyQualifiedName : Target.Name,
                ["type"] = Target.GetType().FullName,
                ["editableBackReferences"] = new JArray(_ownedObjects
                    .OfType<IManagedObject>()
                    .Select(item => item is FieldElement ownedField ? ownedField.FullyQualifiedName : item.Name))
            }.ToString(Formatting.None);
        }

        public void ValidateTool(ToolRequest request)
        {
            EnsureActive();
            var args = request.Arguments ?? new JObject();
            switch (request.ToolName)
            {
                case "UpdateFlowBlock":
                    EnsureWritable(Registry.GetFlowBlocks().FirstOrDefault(item =>
                        string.Equals(item.Name, args.Value<string>("name"), StringComparison.OrdinalIgnoreCase))!);
                    return;
                case "UpdateManagedObject":
                    EnsureWritable(Registry.GetManagedObjects().FirstOrDefault(item =>
                        string.Equals(item is FieldElement field ? field.FullyQualifiedName : item.Name,
                            args.Value<string>("name"), StringComparison.OrdinalIgnoreCase))!);
                    return;
                case "CreateField":
                    if (Target is not BaseResultFlowBlock source ||
                        !string.Equals(source.Name, args.Value<string>("sourceFlowBlockName"), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("CreateField is restricted to the current result FlowBlock.");
                    return;
                case "CreateManagedObject":
                    var managedObjectType = ToolHandlerUtilities.ResolveType(args.Value<string>("typeFullName"));
                    if (managedObjectType == null || !CanCreateManagedObject(managedObjectType))
                        throw new InvalidOperationException("Only types supported by an owned back-reference collection on the current FlowBlock can be created here.");
                    return;
                case "BatchExecuteToolRequests":
                case "ExecuteExternalProject":
                case "LoadExplanation":
                case "InspectOutputFieldValue":
                    return;
            }

            if ((request.ToolName.StartsWith("Get", StringComparison.Ordinal) && request.ToolName != "GetProjectJson") ||
                request.ToolName.StartsWith("Search", StringComparison.Ordinal))
                return;

            throw new InvalidOperationException($"Tool '{request.ToolName}' is unavailable in component mode. Use the project assistant for changes outside this editor.");
        }

        private bool CanCreateManagedObject(Type itemType)
        {
            if (Target is not BaseFlowBlock flowBlock)
                return false;

            foreach (var name in flowBlock.GetBackReferencedPropertyNames())
            {
                if (name == nameof(BaseFlowBlock.DefinedManagedObjects))
                    continue;

                var property = flowBlock.GetType().GetProperty(name);
                var collection = property?.GetValue(flowBlock) as IList;
                if (collection == null || collection.IsReadOnly || collection.IsFixedSize)
                    continue;

                var collectionInterface = collection.GetType().GetInterfaces().FirstOrDefault(type =>
                    type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>));
                if (collectionInterface?.GetGenericArguments()[0].IsAssignableFrom(itemType) == true)
                    return true;
            }
            return false;
        }

        public HashSet<IManagedObject> CaptureRegisteredObjects() =>
            new(Registry.GetManagedObjects(), ReferenceEqualityComparer.Instance);

        public void TrackCreatedObjects(HashSet<IManagedObject> previousObjects)
        {
            var added = Registry.GetManagedObjects().Where(item => !previousObjects.Contains(item)).ToList();
            foreach (var item in added)
            {
                _createdObjects.Add(item);
                _ownedObjects.Add(item);
                Registry.RegisterManagedObject(item);
            }
        }

        public IReadOnlyList<IManagedObject> GetUnlinkedCreatedObjects()
        {
            var flowBlock = Target as BaseFlowBlock;
            return _createdObjects.Where(item => flowBlock?.HasBackReference(item) != true).ToList();
        }

        public void RefreshCreationWarning(JObject result)
        {
            var unlinked = GetUnlinkedCreatedObjects();
            if (unlinked.Count == 0)
            {
                result.Remove("componentScopeWarning");
                result.Remove("unlinkedCreatedObjects");
                return;
            }

            result["componentScopeWarning"] = "New objects are still unassigned. Link them to an owned back-reference or defining field property " +
                "of the current FlowBlock using Update before completing the request. They are registered in this editor transaction, not automatically assigned.";
            result["unlinkedCreatedObjects"] = new JArray(unlinked.Select(item =>
                item is FieldElement field ? field.FullyQualifiedName : item.Name));
        }

        public object PrepareLink(object owner, PropertyInfo property, object value)
        {
            var field = value as FieldElement;
            var association = property.GetCustomAttribute<FlowBlox.Core.Attributes.FlowBloxAssociationAttribute>();
            if (field != null && 
                association != null && 
                association.Operations.HasFlag(FlowBlox.Core.Attributes.UIOperations.Create) &&
                !association.Operations.HasFlag(FlowBlox.Core.Attributes.UIOperations.Link) &&
                Target is BaseFlowBlock && 
                !ReferenceEquals(field.Source, Target))
            {
                throw new InvalidOperationException("A defining field association requires a field owned by the current FlowBlock.");
            }

            if (ReferenceEquals(owner, Target) && Target is BaseFlowBlock flowBlock &&
                flowBlock.GetBackReferencedPropertyNames().Contains(property.Name, StringComparer.Ordinal) &&
                value is IManagedObject && !_ownedObjects.Contains(value))
            {
                value = _appendWorkingCopy(value);
                _ownedObjects.Add(value);
            }
            return value;
        }

    }
}
