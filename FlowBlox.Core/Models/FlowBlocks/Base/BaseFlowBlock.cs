using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Events;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.Base.DatasetSelection;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Runtime.WorkItems;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Models.Testing;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using FlowBlox.Core.Util.Fields;
using System.Transactions;
using System.Collections.Generic;
using System.Collections;
using FlowBlox.Core.Models.Generators;
using FlowBlox.Enums;

namespace FlowBlox.Core.Models.FlowBlocks.Base
{
    [FlowBloxUIGroup("Global_Groups_Default", -10, ControlAlignment.Top)]
    [FlowBloxUIGroup("BaseFlowBlock_Groups_Dependencies", 10)]
    [FlowBloxUIGroup("BaseFlowBlock_Groups_Input", 20)]
    [FlowBloxUIGroup("Global_Groups_Requirements", 30)]
    [FlowBloxUIGroup("BaseFlowBlock_Groups_Tests", 40)]
    [Serializable()]
    public abstract class BaseFlowBlock : FlowBloxComponent
    {
        public delegate void OnPropertyValuesChangedEventHandler();
        public delegate void WarnEventHandler(BaseRuntime runtime, string message);
        public delegate void ErrorEventHandler(BaseRuntime runtime, string message);
        public delegate void UndoNotificationEventHandler(BaseRuntime runtime);
        public delegate void FlagsChangedEventHandler(BaseRuntime runtime, FlowBlockFlags flowBlockFlags);

        public delegate void FlowBlockNameChangeEventHandler(BaseFlowBlock flowBlock, string oldName, string newName);
        public delegate void PropertyChangeEventHandler(string propertyName);
        public delegate void ReferencedFlowBlocksChangedEventHandler(ReferencedFlowBlocksChangedEventArgs eventArgs);

        public delegate void IterationStartHandler(BaseRuntime runtime);
        public delegate void IterationEndHandler(BaseRuntime runtime);

        public delegate void OnBeforeInputProcessingEventHandler();

        public event OnPropertyValuesChangedEventHandler OnPropertyValuesChanged;
        public event ReferencedFlowBlocksChangedEventHandler ReferencedFlowBlocksChanged;
        public event UndoNotificationEventHandler OnUndoWarn;
        public event UndoNotificationEventHandler OnUndoError;
        public event WarnEventHandler OnWarn;
        public event ErrorEventHandler OnError;
        public event FlagsChangedEventHandler OnFlagsChanged;
        public event FlowBlockNameChangeEventHandler OnNameChanged;
        public event IterationStartHandler IterationStart;
        public event IterationEndHandler IterationEnd;
        public event OnBeforeInputProcessingEventHandler OnBeforeInputProcessing;

        internal void RaiseIterationStart(BaseRuntime runtime) => IterationStart?.Invoke(runtime);
        private static readonly ThreadLocal<List<IRuntimeWorkItem>> IterationEndWorkItemCache = new(() => null);

        internal void RaiseIterationEnd(BaseRuntime runtime)
        {
            var cache = IterationEndWorkItemCache.Value;
            if (cache == null)
                cache = new List<IRuntimeWorkItem>();

            cache.Clear();
            IterationEndWorkItemCache.Value = cache;

            IterationEnd?.Invoke(runtime);
            runtime.TaskRunner.EnqueueBatchInExecutionOrder(cache);
            cache.Clear();
        }

        /// <summary>
        /// Completes a handled execution failure without interrupting the flow contract.
        /// </summary>
        internal virtual void CompleteFailedExecution(BaseRuntime runtime) => ExecuteNextFlowBlocks(runtime);

        public void CreateNotification(BaseRuntime runtime, Enum notificationEnumValue, Exception e = null)
        {
            CreateNotification(runtime, notificationEnumValue, notificationEnumValue.GetDisplayName(), e);
        }

        /// <summary>
        /// Creates a scoped notification with a contextual message.
        /// The enum continues to control the notification's configured severity.
        /// </summary>
        public void CreateNotification(
            BaseRuntime runtime,
            Enum notificationEnumValue,
            string message,
            Exception e)
        {
            var notificationType = GetCurrentNotificationType(notificationEnumValue);
            if (notificationType == NotificationType.None)
                return;

            var memberInfo = notificationEnumValue.GetType().GetMember(notificationEnumValue.ToString()).Single();
            var attribute = memberInfo.GetCustomAttribute<FlowBloxNotificationAttribute>();
            if (attribute == null)
                throw new InvalidOperationException(
                    $"Missing {nameof(FlowBloxNotificationAttribute)} on enum value '{notificationEnumValue}'. " +
                     "Please annotate the enum with [FlowBlockNotification(...)] to define its NotificationType.");

            if (notificationType == NotificationType.Warning)
            {
                runtime.Report(message, FlowBloxLogLevel.Warning, e);
                runtime.NotifyWarning(this, message, e);
                OnWarn?.Invoke(runtime, message);
            }
            else
            {
                runtime.Report(message, FlowBloxLogLevel.Error, e);
                runtime.NotifyError(this, message, e);
                OnError?.Invoke(runtime, message);
            }
        }

        private void UndoWarn(BaseRuntime runtime)
        {
            OnUndoWarn?.Invoke(runtime);
        }

        private void UndoError(BaseRuntime runtime)
        {
            OnUndoError?.Invoke(runtime);
        }

        [DeepCopierIgnore()]
        public virtual List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = new List<Type>
                {
                    typeof(BaseFlowBlockNotifications)
                };
                return notificationTypes;
            }
        }

        [DeepCopierIgnore()]
        public ObservableCollection<OverriddenNotificationEntry> OverriddenNotificationEntries { get; set; }

        [JsonIgnore]
        public bool HasOverriddenNotifications =>
            OverriddenNotificationEntries?.Any(x => x?.Overrides?.Count > 0) == true;

        public NotificationType GetCurrentNotificationType(Enum enumValue)
        {
            var enumType = enumValue.GetType();
            var enumKey = Convert.ToInt64(enumValue);

            var entry = OverriddenNotificationEntries
                .FirstOrDefault(x => x.TypeName == enumType.AssemblyQualifiedName);

            if (entry != null && entry.Overrides.TryGetValue(enumKey, out var overridden))
                return overridden;

            var memberInfo = enumType.GetMember(enumValue.ToString()).Single();
            var attribute = memberInfo.GetCustomAttribute<FlowBloxNotificationAttribute>();
            if (attribute == null)
                throw new InvalidOperationException($"Missing FlowBlockNotificationAttribute on {enumValue}");

            return attribute.NotificationType;
        }

        public void OverrideNotificationType(Enum enumValue, NotificationType notificationType)
        {
            var enumType = enumValue.GetType();
            var enumKey = Convert.ToInt64(enumValue);

            var entry = OverriddenNotificationEntries
                .FirstOrDefault(x => x.TypeName == enumType.AssemblyQualifiedName);

            if (notificationType == GetDefaultNotificationType(enumValue))
            {
                if (entry != null && entry.Overrides.Remove(enumKey) && entry.Overrides.Count == 0)
                    OverriddenNotificationEntries.Remove(entry);

                OnPropertyChanged(nameof(OverriddenNotificationEntries));
                OnPropertyChanged(nameof(HasOverriddenNotifications));
                return;
            }

            if (entry == null)
            {
                entry = new OverriddenNotificationEntry
                {
                    TypeName = enumType.AssemblyQualifiedName
                };
                OverriddenNotificationEntries.Add(entry);
            }

            entry.Overrides[enumKey] = notificationType;
            OnPropertyChanged(nameof(OverriddenNotificationEntries));
            OnPropertyChanged(nameof(HasOverriddenNotifications));
        }

        public void ResetNotificationOverrides()
        {
            if (OverriddenNotificationEntries == null || OverriddenNotificationEntries.Count == 0)
                return;

            OverriddenNotificationEntries.Clear();
            OnPropertyChanged(nameof(OverriddenNotificationEntries));
            OnPropertyChanged(nameof(HasOverriddenNotifications));
        }

        private NotificationType GetDefaultNotificationType(Enum enumValue)
        {
            var memberInfo = enumValue.GetType().GetMember(enumValue.ToString()).Single();
            var attribute = memberInfo.GetCustomAttribute<FlowBloxNotificationAttribute>();
            if (attribute == null)
                throw new InvalidOperationException($"Missing FlowBlockNotificationAttribute on {enumValue}");

            return attribute.NotificationType;
        }

        public void Flag(BaseRuntime runtime, FlowBlockFlags flags)
        {
            this.CurrentFlags = flags;
            if (runtime?.ExecutionFlowEnabled != false)
                OnFlagsChanged?.Invoke(runtime, flags);
        }

        public void ResetNotifications(BaseRuntime runtime)
        {
            this.UndoWarn(runtime);
            this.UndoError(runtime);
            this.Flag(runtime, FlowBlockFlags.None);
        }

        public void PropertyValuesChanged() => OnPropertyValuesChanged?.Invoke();

        internal const string Regex_FullyQualifiedFieldNames = "\\$[A-Za-z0-9_-öäüÖÄÜ]*::[A-Za-z0-9_-öäüÖÄÜ]*";
        internal const string Regex_ValidateIdentifier = "^[A-Za-z0-9_-öäüÖÄÜ]*$";
        private const string PreviousFlowBlocksActivationConditionsNotMetMessage = "The activation conditions of the previous FlowBlocks were not met.";

        [JsonIgnore()]
        public virtual string NamePrefix
        {
            get
            {
                string typeName = this.GetType().Name;
                if (typeName.EndsWith(GlobalConstants.FlowBlockTypeNameSuffix))
                {
                    var length = typeName.Length - GlobalConstants.FlowBlockTypeNameSuffix.Length;
                    return typeName.Substring(0, length) + '_';
                }
                else
                {
                    return typeName;
                }
            }
        }

        [JsonIgnore()]
        public virtual bool CreateNumericNameSuffix => true;

        private Point _location;
        public Point Location
        {
            get => _location;
            set
            {
                if (_location == value)
                    return;

                _location = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Runtime/UI-measured element size metadata for diagnostics and tooling.
        /// This value is not used to render or layout the UI.
        /// </summary>
        private Size _size;
        public Size Size
        {
            get => _size;
            set
            {
                if (_size == value)
                    return;

                _size = value;
                OnPropertyChanged();
            }
        }

        [FlowBloxUI(UISelectionButtons.None)]
        public int ExecutionIndex { get; set; } = -1;

        [JsonProperty("ElementIndex")]
        private int LegacyElementIndex
        {
            set => ExecutionIndex = value;
        }

        private bool _isNotExecuted;
        [FlowBloxUI(UISelectionButtons.None)]
        public bool IsNotExecuted
        {
            get => _isNotExecuted;
            set
            {
                if (_isNotExecuted == value)
                    return;

                _isNotExecuted = value;
                OnPropertyChanged();
            }
        }

        [JsonIgnore()]
        public virtual bool CanGoBack => true;

        [JsonIgnore()]
        public virtual bool CanBeReferenced => true;

        [FlowBloxUI(UISelectionButtons.None)]
        public bool BreakPoint { get; set; }

        protected BaseFlowBlock()
        {
            this.InputBehaviorAssignments = new ObservableCollection<InputBehaviorAssignment>();
            this.ReferencedFlowBlocks = new ObservableCollection<BaseFlowBlock>();
            this.ActivationConditions = new ObservableCollection<LogicalCondition>();
            this.TestDefinitions = new ObservableCollection<FlowBloxTestDefinition>();
            this.GenerationStrategies = new ObservableCollection<FlowBloxGenerationStrategyBase>();
            this._passedResults = new FlowBlockPassedResultBuffer();

            this.InheritRequirementsNotMet = true;

            this.OverriddenNotificationEntries = new ObservableCollection<OverriddenNotificationEntry>();
        }

        private string _name;

        /// <summary>
        /// The element identifier of the grid element.
        /// </summary>
        [Display(Name = "BaseFlowBlock_Name", ResourceType = typeof(FlowBloxTexts), GroupName = "Global_Groups_Default", Order = -10)]
        [CustomValidation(typeof(FlowBloxComponent), nameof(ValidateName))]
        [Required()]
        [FlowBloxUI(UISelectionButtons.None)]
        public override string Name
        {
            get
            {
                return _name;
            }
            set
            {
                string oldName = _name;
                string newName = value;

                if (oldName != newName)
                {
                    this._name = newName;
                    OnNameChanged?.Invoke(this, oldName, newName);
                }
            }
        }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public BaseFlowBlock Parent { get; protected set; }

        public abstract FlowBlockCategory GetCategory();

        public abstract FlowBlockCardinalities GetInputCardinality();

        private ObservableCollection<BaseFlowBlock> _referencedFlowBlocks = new ObservableCollection<BaseFlowBlock>();

        /// <summary>
        /// Returns the flow blocks that are connected via flow logic (e.g., based on execution path or data flow).
        /// Does not include flow blocks that are associated via property definitions — use <see cref="GetAssociatedFlowBlocks"/> for that.
        /// </summary>
        [Display(Name = "BaseFlowBlock_ReferencedElements", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseFlowBlock_Groups_Dependencies", Order = 0)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(BaseFlowBlock.Name) })]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(BaseFlowBlock.GetPossibleReferencedElements), DisplayMember = nameof(Name))]
        public virtual ObservableCollection<BaseFlowBlock> ReferencedFlowBlocks
        {
            get => _referencedFlowBlocks;
            set
            {
                if (value == null)
                    throw new NotSupportedException($"Setting {nameof(ReferencedFlowBlocks)} to null is not supported.");

                var previousReferences = _referencedFlowBlocks?.ToList() ?? new List<BaseFlowBlock>();

                if (_referencedFlowBlocks != null)
                    _referencedFlowBlocks.CollectionChanged -= _referencedFlowBlocks_CollectionChanged;

                _referencedFlowBlocks = value;

                _referencedFlowBlocks.CollectionChanged -= _referencedFlowBlocks_CollectionChanged;
                _referencedFlowBlocks.CollectionChanged += _referencedFlowBlocks_CollectionChanged;

                OnAfterReferencedFlowBlocksChanged(
                    value.Except(previousReferences).ToList(),
                    previousReferences.Except(value).ToList());
            }
        }

        /// <summary>
        /// Returns the flow blocks that directly follow this flow block through <see cref="ReferencedFlowBlocks"/>.
        /// Prefer <see cref="GetNextFlowBlocks"/> in code; this property exists for serialization scenarios.
        /// </summary>
        [DeepCopierIgnore()]
        public List<BaseFlowBlock> NextFlowBlocks => GetNextFlowBlocks();

        private void _referencedFlowBlocks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            var addedFlowBlocks = e.NewItems?.OfType<BaseFlowBlock>().ToList() ?? new List<BaseFlowBlock>();
            var removedFlowBlocks = e.OldItems?.OfType<BaseFlowBlock>().ToList() ?? new List<BaseFlowBlock>();
            OnAfterReferencedFlowBlocksChanged(addedFlowBlocks, removedFlowBlocks);
        }

        /// <summary>
        /// Can be overridden to define managed objects that are defined by this flow block.
        /// </summary>
        public virtual List<IManagedObject> DefinedManagedObjects
        {
            get
            {
                return new List<IManagedObject>();
            }
        }

        /// <summary>
        /// Returns the names of properties that may contain managed objects
        /// with a back reference to this flow block.
        /// </summary>
        /// <returns>A sequence of property names.</returns>
        public virtual IEnumerable<string> GetBackReferencedPropertyNames()
        {
            yield return nameof(TestDefinitions);
            yield return nameof(DefinedManagedObjects);
        }

        /// <summary>
        /// Determines whether the specified managed object is referenced
        /// by one of the configured back reference properties.
        /// </summary>
        /// <param name="managedObject">The managed object to check.</param>
        /// <returns><c>true</c> if a back reference exists; otherwise <c>false</c>.</returns>
        public virtual bool HasBackReference(IManagedObject managedObject)
        {
            if (managedObject == null)
                return false;

            var type = GetType();

            foreach (var propertyName in GetBackReferencedPropertyNames() ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                    continue;

                var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || !property.CanRead)
                    continue;

                var value = property.GetValue(this);
                if (value is not IEnumerable enumerable)
                    continue;

                foreach (var item in enumerable)
                {
                    if (ReferenceEquals(item, managedObject) || object.Equals(item, managedObject))
                        return true;
                }
            }

            return false;
        }

        protected virtual void OnAfterReferencedFlowBlocksChanged(
            IReadOnlyCollection<BaseFlowBlock> addedFlowBlocks = null,
            IReadOnlyCollection<BaseFlowBlock> removedFlowBlocks = null)
        {
            RefreshNotExecutedState();

            if (!this.IsLoaded)
                return;

            OnPropertyChanged(nameof(IterationContext));
            OnPropertyChanged(nameof(HasIterationContext));
            SyncReferencedFlowBlocksWithInputBehaviorAssignments();
            ReferencedFlowBlocksChanged?.Invoke(new ReferencedFlowBlocksChangedEventArgs(
                this,
                addedFlowBlocks ?? Array.Empty<BaseFlowBlock>(),
                removedFlowBlocks ?? Array.Empty<BaseFlowBlock>()));
            NotifyComponentChanged();
        }

        public void RefreshNotExecutedState()
            => IsNotExecuted = ShouldBeMarkedAsNotExecuted();

        private bool ShouldBeMarkedAsNotExecuted()
        {
            if (this is StartFlowBlock or NoteFlowBlock)
                return false;

            return ReferencedFlowBlocks == null || !ReferencedFlowBlocks.Any();
        }

        private bool _isInputBehaviorSynchronizationSuppressed;

        /// <summary>
        /// Suppresses automatic synchronization of input behavior assignments.
        /// </summary>
        public void SuppressInputBehaviorSynchronization() => _isInputBehaviorSynchronizationSuppressed = true;

        /// <summary>
        /// Enables automatic input behavior synchronization without replaying skipped changes.
        /// </summary>
        public void RestoreInputBehaviorSynchronization() => _isInputBehaviorSynchronizationSuppressed = false;

        private void SyncReferencedFlowBlocksWithInputBehaviorAssignments()
        {
            // Preserve explicitly supplied assignments during initialization or bulk updates.
            if (!IsLoaded || _isInputBehaviorSynchronizationSuppressed)
                return;

            // Input behaviors are relevant only within an iteration context.
            // Remove all existing assignments when the iteration context is no longer available.
            if (!HasIterationContext)
            {
                if (InputBehaviorAssignments.Count > 0)
                {
                    InputBehaviorAssignments.Clear();
                    OnPropertyChanged(nameof(InputBehaviorAssignments));
                }

                return;
            }

            // Reevaluate input behaviors only when the represented referenced flow blocks
            // have actually changed.
            var previousAssignments = InputBehaviorAssignments.Where(assignment => assignment?.FlowBlock != null).ToList();

            if (!SyncInputBehaviorAssignmentReferences())
                return;

            SyncInputBehaviorsAfterReferenceChange(previousAssignments);
            OnPropertyChanged(nameof(InputBehaviorAssignments));
        }

        private bool SyncInputBehaviorAssignmentReferences()
        {
            bool anyChange = false;

            // Remove assignments whose source FlowBlock no longer exists.
            foreach (var orphan in InputBehaviorAssignments.Where(assignment => assignment?.FlowBlock == null).ToList())
            {
                InputBehaviorAssignments.Remove(orphan);
                anyChange = true;
            }

            // Create missing assignments for newly added references.
            foreach (var missingFb in _referencedFlowBlocks.Where(x => !this.InputBehaviorAssignments.Any(y => y.FlowBlock == x)))
            {
                var assignment = new InputBehaviorAssignment()
                {
                    FlowBlock = missingFb
                };
                if (IsLoaded)
                    assignment.OnAfterLoad();
                this.InputBehaviorAssignments.Add(assignment);

                anyChange = true;
            }

            // Remove assignments for references that no longer exist.
            foreach (var unusedInputBehaviorAssignment in this.InputBehaviorAssignments
                .Where(x => !_referencedFlowBlocks.Any(y => y == x.FlowBlock))
                .ToList())
            {
                this.InputBehaviorAssignments.Remove(unusedInputBehaviorAssignment);
                anyChange = true;
            }

            // Remove duplicate assignments for each referenced flow block.
            var duplicates = this.InputBehaviorAssignments
                .GroupBy(x => x.FlowBlock)
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.Skip(1))
                .ToList();

            foreach (var duplicate in duplicates)
            {
                this.InputBehaviorAssignments.Remove(duplicate);
                anyChange = true;
            }

            return anyChange;
        }

        private void SyncInputBehaviorsAfterReferenceChange(IReadOnlyCollection<InputBehaviorAssignment> previousAssignments)
        {
            if (!AutomaticInputBehaviorAssignment)
                return;

            var orderedAssignments = _referencedFlowBlocks
                .Select(flowBlock => InputBehaviorAssignments.Single(assignment => assignment.FlowBlock == flowBlock))
                .ToList();

            // Initial inputs: Cross for one input, First for multiple inputs.
            if (previousAssignments.Count == 0)
            {
                var initialBehavior = orderedAssignments.Count == 1 ? InputBehavior.Cross : InputBehavior.First;
                foreach (var assignment in orderedAssignments)
                    assignment.Behavior = initialBehavior;
                return;
            }

            // One to multiple: Cross/CrossValid plus First/FirstValid inputs becomes First.
            if (previousAssignments.Count == 1 && orderedAssignments.Count > 1)
            {
                var firstBehavior = orderedAssignments[0].Behavior;
                var firstInputUsesCross = firstBehavior == InputBehavior.Cross || firstBehavior == InputBehavior.CrossValid;
                var remainingInputsUseFirst = orderedAssignments.Skip(1).All(assignment =>
                    assignment.Behavior == InputBehavior.First || assignment.Behavior == InputBehavior.FirstValid);

                if (firstInputUsesCross && remainingInputsUseFirst)
                {
                    foreach (var assignment in orderedAssignments)
                        assignment.Behavior = InputBehavior.First;
                    return;
                }
            }

            // Multiple to one: uniform or mixed First/FirstValid inputs become Cross.
            if (previousAssignments.Count > 1 && orderedAssignments.Count == 1)
            {
                var allPreviousInputsUseFirst = previousAssignments.All(assignment =>
                    assignment.Behavior == InputBehavior.First || assignment.Behavior == InputBehavior.FirstValid);
                if (allPreviousInputsUseFirst)
                    orderedAssignments[0].Behavior = InputBehavior.Cross;
                return;
            }

            // New default inputs inherit uniform RowWise/RowWiseValid, preserving Valid filtering.
            var previousBehavior = previousAssignments.First().Behavior;
            if (previousBehavior != InputBehavior.RowWise && previousBehavior != InputBehavior.RowWiseValid)
                return;
            if (!previousAssignments.All(assignment => assignment.Behavior == previousBehavior))
                return;

            foreach (var assignment in orderedAssignments.Except(previousAssignments))
            {
                if (assignment.Behavior == InputBehavior.First)
                    assignment.Behavior = previousBehavior;
            }
        }

        public bool HasIterationContext 
        {
            get
            {
                if (this.IterationContext != null)
                    return true;

                return false;
            }
        }

        [Display(Name = "BaseFlowBlock_AssociatedIterationContext", 
            Description = "BaseFlowBlock_AssociatedIterationContext_Tooltip", 
            ResourceType = typeof(FlowBloxTexts), 
            GroupName = "BaseFlowBlock_Groups_Input", Order = 0)]
        [AssociatedFlowBlockResolvableCustom(nameof(IterationContext), nameof(CanDisplayAssociatedIterationContextHint))]
        [JsonProperty("AssociatedIterationContext")]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleIterationContexts), DisplayMember = nameof(Name))]
        public BaseFlowBlock AssociatedIterationContext
        {
            get => _associatedIterationContext;
            set
            {
                ValidateAssociatedIterationContext(value);
                if (ReferenceEquals(_associatedIterationContext, value))
                    return;

                _associatedIterationContext = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IterationContext));
                OnPropertyChanged(nameof(HasIterationContext));

                if (IsLoaded)
                    SyncReferencedFlowBlocksWithInputBehaviorAssignments();
            }
        }

        private BaseFlowBlock _associatedIterationContext;

        [JsonProperty("AssociatedInputReference")]
        private BaseFlowBlock LegacyAssociatedInputReference
        {
            set
            {
                if (AssociatedIterationContext == null)
                    AssociatedIterationContext = value;
            }
        }

        public virtual bool CanDisplayAssociatedIterationContextHint()
        {
            return this.ReferencedFlowBlocks.Count() > 1 && 
                   this.AssociatedIterationContext == null;
        }

        public virtual BaseFlowBlock IterationContext
        {
            get
            {
                if (AssociatedIterationContext != null)
                    return AssociatedIterationContext;

                if (this.ReferencedFlowBlocks.Count() > 1)
                    return CommonFlowBlockResolver.FindCommonFlowBlock(this);

                return null;
            }
        }

        private void ValidateAssociatedIterationContext(BaseFlowBlock value)
        {
            if (value == null)
                return;

            if (ReferenceEquals(value, this))
                throw new InvalidOperationException("AssociatedIterationContext must reference another flow block.");

            if (IsDirectPredecessorFlowBlock(value))
            {
                throw new InvalidOperationException(
                    $"AssociatedIterationContext \"{value.Name}\" is not allowed for \"{Name}\". " +
                    "It must not reference a direct predecessor flow block.");
            }
        }

        private bool IsDirectPredecessorFlowBlock(BaseFlowBlock candidate)
        {
            if (candidate == null)
                return false;

            var registry = FlowBloxRegistryProvider.GetRegistry();
            if (registry == null)
                return false;

            return registry.GetPreviousElements(this)
                .OfType<BaseFlowBlock>()
                .Any(previous => ReferenceEquals(previous, candidate));
        }

        [Display(Name = "BaseFlowBlock_InputIgnoreDuplicates", Description = "BaseFlowBlock_InputIgnoreDuplicates_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseFlowBlock_Groups_Input", Order = 1)]
        [ActivationCondition(MemberName = nameof(HasIterationContext), Value = true)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool InputIgnoreDuplicates { get; set; }

        [Display(Name = "BaseFlowBlock_AutomaticInputBehaviorAssignment", Description = "BaseFlowBlock_AutomaticInputBehaviorAssignment_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseFlowBlock_Groups_Input", Order = 2)]
        [ActivationCondition(MemberName = nameof(HasIterationContext), Value = true)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool AutomaticInputBehaviorAssignment { get; set; } = true;

        [Display(Name = "BaseFlowBlock_InputBehaviorAssignments", Description = "BaseFlowBlock_InputBehaviorAssignments_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseFlowBlock_Groups_Input", Order = 3)]
        [ActivationCondition(MemberName = nameof(HasIterationContext), Value = true)]
        [ConditionallyRequired]
        [FlowBloxAssociation(Operations = UIOperations.Edit)]
        [FlowBloxDataGrid]
        public ObservableCollection<InputBehaviorAssignment> InputBehaviorAssignments { get; set; }

        public override DeletionReviewStatus GetDeletionReviewStatus(
            object owner, PropertyInfo property, DeletionImpact impact, PropertyInfo componentProperty)
        {
            if (componentProperty?.Name == nameof(InputBehaviorAssignments) &&
                owner is InputBehaviorAssignment && 
                property.Name == nameof(InputBehaviorAssignment.FlowBlock) &&
                impact == DeletionImpact.DirectReferenceRemoved)
                return DeletionReviewStatus.Information;

            return base.GetDeletionReviewStatus(owner, property, impact, componentProperty);
        }

        [Display(Name = "BaseFlowBlock_InheritRequirementsNotMet", ResourceType = typeof(FlowBloxTexts), GroupName = "Global_Groups_Requirements", Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool InheritRequirementsNotMet { get; set; }

        [Display(Name = "BaseFlowBlock_ActivationConditions", Description = "BaseFlowBlock_ActivationConditions_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "Global_Groups_Requirements", Order = 2)]
        [FlowBloxDataGrid(
            ColumnMemberNames = [
            nameof(LogicalCondition.LogicalOperator),
            nameof(LogicalCondition.DisplayName)
            ])]
        [FlowBloxAssociation(
            CreatableTypes = new[] {
                typeof(FieldLogicalComparisonCondition),
                typeof(LogicalGroupCondition) })]
        public ObservableCollection<LogicalCondition> ActivationConditions { get; set; } = new ObservableCollection<LogicalCondition>();

        private ObservableCollection<FlowBloxTestDefinition> _testDefinitions = new();

        [Display(Name = "BaseFlowBlock_TestDefinitions", Description = "BaseFlowBlock_TestDefinitions_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseFlowBlock_Groups_Tests", Order = 0)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(FlowBloxTestDefinition.Name) })]
        [FlowBloxAssociation(
            Operations = UIOperations.Link | UIOperations.Unlink | UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleTestDefinitions), DisplayMember = nameof(FlowBloxTestDefinition.Name))]
        public ObservableCollection<FlowBloxTestDefinition> TestDefinitions
        {
            get => _testDefinitions;
            set
            {
                if (value == null)
                    throw new NotSupportedException($"Setting {nameof(TestDefinitions)} to null is not supported.");

                if (_testDefinitions != null)
                    _testDefinitions.CollectionChanged -= TestDefinitions_CollectionChanged;

                _testDefinitions = value;

                // ensure a single subscription even when the same collection instance is assigned repeatedly
                _testDefinitions.CollectionChanged -= TestDefinitions_CollectionChanged;
                _testDefinitions.CollectionChanged += TestDefinitions_CollectionChanged;

                OnPropertyChanged();
            }
        }

        private void TestDefinitions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<FlowBloxTestDefinition>())
                {
                    EnsureTestDefinitionSynchronized(item);
                }
            }

            OnPropertyChanged(nameof(TestDefinitions));
        }

        private readonly FlowBloxTestDefinitionSynchronizer _testDefinitionSynchronizer = new FlowBloxTestDefinitionSynchronizer();

        private void EnsureTestDefinitionSynchronized(FlowBloxTestDefinition? testDefinition)
        {
            if (!IsLoaded || testDefinition == null)
                return;

            _testDefinitionSynchronizer.Synchronize(testDefinition, this);
        }

        [Display(Name = "BaseFlowBlock_GenerationStrategies", Description = "BaseFlowBlock_GenerationStrategies_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "BaseFlowBlock_Groups_Tests", Order = 1)]
        [ActivationCondition(ActivationMethod = nameof(IsGenerationStrategiesVisible))]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(FlowBloxGenerationStrategyBase.Name) })]
        [CustomValidation(typeof(BaseFlowBlock), nameof(ValidateGenerationStrategies))]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public ObservableCollection<FlowBloxGenerationStrategyBase> GenerationStrategies { get; set; }

        public bool IsGenerationStrategiesVisible()
        {
            return FlowBloxSupportedTypesResolver
                .ResolveSupportedTypes(this, typeof(FlowBloxGenerationStrategyBase))
                .Any();
        }

        public static ValidationResult ValidateGenerationStrategies(ObservableCollection<FlowBloxGenerationStrategyBase> generationStrategies, ValidationContext context)
        {
            string message = "";

            foreach (var generationStrategy in generationStrategies)
            {
                if (!generationStrategy.CanExecute(out var testDefinitionToMessages, out var messages))
                {
                    message = string.Join(Environment.NewLine, messages);

                    foreach (var kvp in testDefinitionToMessages)
                    {
                        var testCase = kvp.Key;
                        var messagesForTestCase = kvp.Value;

                        message = string.Join(Environment.NewLine, message, $"Problems with test case \"{testCase.Name}\": {string.Join(", ", messagesForTestCase)}");
                    }
                }
            }

            if (string.IsNullOrEmpty(message))
                return ValidationResult.Success;

            return new ValidationResult(message, [context.MemberName]);
        }

        public List<FlowBloxTestDefinition> GetPossibleTestDefinitions()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var testDefinitions = registry.GetManagedObjects<FlowBloxTestDefinition>();
            return testDefinitions
                .Except(this.TestDefinitions)
                .OrderBy(x => x.Name)
                .ToList();
        }

        /// <summary>
        /// Returns the associated flow blocks that are connected via property definitions.
        /// Does not include flow blocks that are connected via flow logic — use <see cref="ReferencedFlowBlocks"/> for that.
        /// </summary>
        public IEnumerable<BaseFlowBlock> GetAssociatedFlowBlocks() => FlowBlockObjectCollector.CollectFlowBlocks(this);

        protected T GetPreviousFlowBlockOnPath<T>(BaseFlowBlock flowBlock)
            where T : BaseFlowBlock
        {
            return (T)GetPreviousFlowBlockOnPath(flowBlock, [typeof(T)]);
        }

        protected BaseFlowBlock GetPreviousFlowBlockOnPath(BaseFlowBlock flowBlock, Type[] ofTypes)
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            if (registry == null)
                return null;

            return GetPreviousFlowBlockOnPath(registry.GetPreviousElements(flowBlock), ofTypes);
        }

        protected BaseFlowBlock GetPreviousFlowBlockOnPath(IEnumerable<BaseFlowBlock> flowBlocks, Type[] ofTypes)
        {
            var flowBlocksOfType = flowBlocks.OfType<BaseFlowBlock>()
                .Where(x => ofTypes.Any(ofType => ofType == x.GetType()));

            if (flowBlocksOfType.Count() == 1)
                return flowBlocksOfType.Single();
            else if (flowBlocksOfType.Count() > 1)
            {
                var typeNames = string.Join(", ", ofTypes.Select(t => t.Name));
                throw new InvalidOperationException($"Multiple matching flow blocks found on path. Expected at most one of: {typeNames}.");
            }
            else
            {
                return flowBlocks.Select(x => GetPreviousFlowBlockOnPath(FlowBloxRegistryProvider.GetRegistry().GetPreviousElements(x), ofTypes))
                    .ExceptNull()
                    .Distinct()
                    .SingleOrDefault();
            }
        }

        protected override void OnReferencedFieldNameChanged(FieldElement field, string oldFQFieldName, string newFQFieldName)
        {
            PropertyValuesChanged();
            base.OnReferencedFieldNameChanged(field, oldFQFieldName, newFQFieldName);
        }

        public virtual List<BaseFlowBlock> GetPossibleIterationContexts()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            if (registry == null)
                return new List<BaseFlowBlock>();

            var directPredecessors = registry.GetPreviousElements(this)
                .OfType<BaseFlowBlock>()
                .Where(x => x != null)
                .ToList();

            var result = new List<BaseFlowBlock>();
            var visited = new HashSet<BaseFlowBlock>();
            var queue = new Queue<BaseFlowBlock>(directPredecessors);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current == null || !visited.Add(current))
                    continue;

                foreach (var previous in registry.GetPreviousElements(current).OfType<BaseFlowBlock>())
                {
                    if (previous == null)
                        continue;

                    // Only upstream predecessors are allowed in the selection.
                    result.Add(previous);
                    queue.Enqueue(previous);
                }
            }

            return result
                .Where(x => !ReferenceEquals(x, this))
                .Distinct()
                .OrderBy(x => x.Name)
                .ToList();
        }

        public virtual List<BaseFlowBlock> GetPossibleReferencedElements()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            return registry.GetFlowBlocks()
                .Where(x => x.Name != this.Name)
                .Where(x => x.CanBeReferenced)
                .ToList();
        }

        public override List<FieldElement> GetPossibleFieldElements()
        {
            return FlowBloxFieldsResolver.GetFieldsOrderedByReferencedFlowBlocks(this);
        }

        public static string GetValidIdentifier(string identifier)
        {
            identifier = IOUtil.GetValidFileName(identifier);
            while ((identifier.IndexOf("_") == 0) || (identifier.IndexOf("-") == 0))
            {
                identifier = identifier.Substring(1);
            }
            return identifier;
        }

        protected void Wait(BaseRuntime runtime)
        {
            if (runtime.StepwiseExecution)
                runtime.Pause = true;

            if (BreakPoint)
                runtime.Pause = true;

            runtime.HandlePause();

            if (runtime.EffectiveStepTimeunit > 0)
                Thread.Sleep(runtime.EffectiveStepTimeunit);
        }

        public void SetParentElement(object parentElement)
        {
            if (parentElement is BaseFlowBlock)
                this.Parent = (BaseFlowBlock)parentElement;
        }

        public abstract bool Execute(BaseRuntime runtime, object data);

        public List<BaseFlowBlock> GetNextFlowBlocks()
        {
            return FlowBloxRegistryProvider.GetRegistry().GetInputFlowBlocks()
                .Where(x => x.ReferencedFlowBlocks.Contains(this))
                .OrderBy(x => x.ExecutionIndex < 0 ? 1 : 0)
                .ThenBy(x => x.ExecutionIndex)
                .ToList();
        }

        /// <summary>
        /// Schedules the subsequent flow blocks in the current execution layer.
        /// </summary>
        /// <param name="runtime">The runtime that owns the work queue and current execution state.</param>
        public virtual void ExecuteNextFlowBlocks(BaseRuntime runtime)
        {
            ExecuteNextFlowBlocks(runtime, false);
        }

        /// <summary>
        /// Schedules the subsequent flow blocks and can wrap them in a new execution layer.
        /// Recursive calls use the extra layer so iteration-context input collection keeps
        /// recursive results separate from the caller's pending results until iteration end.
        /// </summary>
        /// <param name="runtime">The runtime that owns the work queue and current execution state.</param>
        /// <param name="increaseExecutionLayer">If true, the queued successor work runs one execution layer deeper.</param>
        internal void ExecuteNextFlowBlocks(BaseRuntime runtime, bool increaseExecutionLayer)
        {
            runtime.TaskRunner.ScheduleNext(this, increaseExecutionLayer);
        }

        /// <summary>
        /// Schedules this flow block for another execution with the same parent context.
        /// </summary>
        /// <param name="runtime">The runtime that owns the work queue and current execution state.</param>
        /// <param name="increaseExecutionLayer">If true, the repeated execution runs one execution layer deeper.</param>
        public virtual void Repeat(BaseRuntime runtime, bool increaseExecutionLayer = true)
        {
            runtime.TaskRunner.ScheduleInvocation(this, Parent, increaseExecutionLayer);
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            if (this.HasIterationContext)
                runtime.Report($"FlowBlock \"{Name}\": Iteration context is \"{IterationContext.Name}\". Inputs will be collected until iteration end, then this block will execute.");

            OnUndoWarn?.Invoke(runtime);
            OnUndoError?.Invoke(runtime);

            if (this.IterationContext != null)
            {
                this.IterationContext.IterationStart -= InputReference_IterationStart;
                this.IterationContext.IterationStart += InputReference_IterationStart;

                this.IterationContext.IterationEnd -= InputReference_IterationEnd;
                this.IterationContext.IterationEnd += InputReference_IterationEnd;
            }
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            if (this.IterationContext != null)
            {
                this.IterationContext.IterationStart -= InputReference_IterationStart;
                this.IterationContext.IterationEnd -= InputReference_IterationEnd;
            }
        }

        public bool ValidateRequirements(out List<string> messages)
        {
            messages = new List<string>();
            bool isValid = ValidateRequirements(messages);
            return isValid;
        }

        protected bool ValidateRequirements()
        {
            var messages = new List<string>();
            return ValidateRequirements(messages);
        }

        public IEnumerable<RequiredFieldContext> GetRequiredFieldContexts()
        {
            var result = new List<RequiredFieldContext>();

            if (this.HandleRequirements && this.RequiredFields != null)
            {
                result.AddRange(this.RequiredFields.Select(x => new RequiredFieldContext()
                {
                    FieldElement = x,
                    FlowBloxComponent = this
                }));
            }

            result.AddRange(FlowBlockObjectCollector.CollectRequiredFieldContextsRecursive(this));

            return result;
        }

        /// <summary>
        /// Returns all required fields of this component and its associated managed objects (recursively),
        /// including this instance’s own <see cref="FlowBloxComponent.RequiredFields"/> if <see cref="HandleRequirements"/> is true.
        /// </summary>
        public IEnumerable<FieldElement> GetRequiredFields()
        {
            var result = new List<FieldElement>();

            if (this.HandleRequirements && this.RequiredFields != null)
                result.AddRange(this.RequiredFields);

            result.AddRange(FlowBlockObjectCollector.CollectRequiredFieldContextsRecursive(this)
                .Select(x => x.FieldElement));

            return result;
        }

        private bool EvaluateActivationConditions()
        {
            if (ActivationConditions == null || ActivationConditions.Count == 0)
                return true;

            bool? result = null;

            foreach (var condition in ActivationConditions)
            {
                bool current = condition.Check();
                if (result == null)
                {
                    result = current;
                    continue;
                }

                switch (condition.LogicalOperator)
                {
                    case LogicalOperator.And:
                        result = result.Value && current;
                        break;

                    case LogicalOperator.Or:
                        result = result.Value || current;
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported logical operator: {condition.LogicalOperator}");
                }
            }

            return result ?? true;
        }

        protected virtual bool ValidateRequirements(List<string> messages)
        {
            if (InheritRequirementsNotMet &&
                ReferencedFlowBlocks.Any() &&
                ReferencedFlowBlocks.All(x => x.CurrentFlags.HasFlag(FlowBlockFlags.RequirementsNotMet)))
            {
                messages.Add(PreviousFlowBlocksActivationConditionsNotMetMessage);
                return false;
            }

            if (!EvaluateActivationConditions())
            {
                var summary = string.Join(" ", ActivationConditions.Select((c, i) => i == 0 ?
                    c.DisplayName :
                    $"{(c.LogicalOperator == LogicalOperator.And ? "and" : "or")} {c.DisplayName}"));

                messages.Add($"Activation conditions were not met: {summary}");
                return false;
            }

            // Check RequiredFields
            var missingRequiredFields = GetRequiredFields()
                .Where(FlowBloxFieldHelper.IsResolvedFieldValueEmpty);

            foreach (var field in missingRequiredFields)
            {
                messages.Add($"Required field \"{field?.FullyQualifiedName}\" is not set.");
            }

            return messages.Count == 0;
        }

        private readonly FlowBlockPassedResultBuffer _passedResults;

        private Action _storedExecutor;

        protected virtual bool Invoke(BaseRuntime runtime, object data, Action executor)
        {
            Action preparedExecutor = () =>
            {
                runtime.Focus(this);
                Wait(runtime);
                SetParentElement(data);
                if (!TryValidateExecutionPrerequisites(runtime))
                    return;

                executor();
            };

            if (!runtime.ExecutionFlowEnabled)
                return InvokeExecutor(runtime, preparedExecutor);

            if (this.HasIterationContext)
            {
                var callingFlowBlock = (BaseFlowBlock)data;
                if (callingFlowBlock == null)
                    throw new InvalidOperationException("The calling flow block could not be determined.");

                if (callingFlowBlock is BaseResultFlowBlock)
                {
                    _passedResults.Add(runtime, (BaseResultFlowBlock)callingFlowBlock);
                }

                this._storedExecutor = preparedExecutor;
            }
            else
            {
                return InvokeExecutor(runtime, preparedExecutor);
            }

            return true;
        }

        public enum BaseFlowBlockNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Preconditions not met")]
            PreconditionsNotMet,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "An unexpected error has occurred.")]
            UnexpectedError,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "Setup required")]
            SetupRequired
        }

        /// <summary>
        /// Reports a failed execution prerequisite and completes the block without executing its action.
        /// </summary>
        /// <param name="runtime">The runtime whose notification and result/continuation contract must be preserved.</param>
        /// <param name="exception">The prerequisite failure, including its Quick Start target and detailed cause.</param>
        /// <remarks>
        /// The default emits the compact SetupRequired error and generates an empty result for result blocks,
        /// or schedules successors for non-result blocks. Override this hook when the block needs its own
        /// notification enum or failure-result payload; complete its result/continuation contract exactly once.
        /// </remarks>
        protected virtual void OnExecutionPrerequisiteFailure(
            BaseRuntime runtime,
            ExecutionPrerequisiteException exception)
        {
            CreateNotification(runtime, BaseFlowBlockNotifications.SetupRequired, exception.NotificationMessage, exception);
            CompleteFailedExecution(runtime);
        }

        private bool TryValidateExecutionPrerequisites(BaseRuntime runtime)
        {
            try
            {
                EvaluateExecutionPrerequisites();
                return true;
            }
            catch (ExecutionPrerequisiteException exception)
            {
                OnExecutionPrerequisiteFailure(runtime, exception);
                return false;
            }
        }

        protected virtual bool InvokeExecutor(BaseRuntime runtime, Action executor)
        {
            ResetNotifications(runtime);

            try
            {
                return InvokeWithResolvedProperties(runtime, executor);
            }
            catch (RuntimeCancellationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                CreateNotification(runtime, BaseFlowBlockNotifications.UnexpectedError, exception);
                return false;
            }
        }

        private bool InvokeWithResolvedProperties(BaseRuntime runtime, Action executor)
        {

            runtime.NotifyBeforeFlowBlockValidation(this);
            if (runtime.Aborted)
                return false;

            if (!this.ValidateRequirements(out var requirementMessages))
            {
                var suppressWarningLogAndNotification = requirementMessages?.Count == 1 &&
                    string.Equals(requirementMessages[0], PreviousFlowBlocksActivationConditionsNotMetMessage, StringComparison.Ordinal);

                if (!suppressWarningLogAndNotification)
                {
                    var details = requirementMessages != null && requirementMessages.Count > 0
                        ? string.Join(Environment.NewLine, requirementMessages)
                        : "No details provided.";

                    runtime.Report($"Preconditions not met: {details}", FlowBloxLogLevel.Warning);
                    CreateNotification(runtime, BaseFlowBlockNotifications.PreconditionsNotMet);
                }

                runtime.NotifyPreconditionsNotMet(this, requirementMessages);
                Flag(runtime, FlowBlockFlags.RequirementsNotMet);
                return false;
            }

            ReportUnresolvedFields(runtime);

            runtime.NotifyInvocationStarted(this);
            try
            {
                executor.Invoke();
                return true;
            }
            catch (GridElementExecutionException e)
            {
                CreateNotification(runtime, BaseFlowBlockNotifications.UnexpectedError, e);
                return false;
            }
            catch (RuntimeCancellationException)
            {
                throw;
            }
            catch (Exception e)
            {
                CreateNotification(runtime, BaseFlowBlockNotifications.UnexpectedError, e);
                return false;
            }
            finally
            {
                runtime.NotifyInvocationFinished(this);
            }
        }

        // Cache associations per runtime, never values; completed runtimes are not retained.
        private readonly ConditionalWeakTable<BaseRuntime, FieldElement[]> _associatedFieldsByRuntime = new();

        private IReadOnlyList<FieldElement> GetAssociatedFieldsForRuntime(BaseRuntime runtime)
        {
            lock (_associatedFieldsByRuntime)
            {
                if (!_associatedFieldsByRuntime.TryGetValue(runtime, out var fields))
                {
                    fields = GetAssociatedFields().ToArray();
                    _associatedFieldsByRuntime.Add(runtime, fields);
                }

                return fields;
            }
        }

        private void ReportUnresolvedFields(BaseRuntime runtime)
        {
            foreach (var field in GetAssociatedFieldsForRuntime(runtime))
            {
                var resolvedValue = FlowBloxFieldHelper.ResolveFieldValue(field);

                foreach (var reference in FlowBloxFieldHelper.GetUnresolvedFieldReferences(resolvedValue))
                {
                    runtime.Report(
                        $"Possible unresolved field reference '{reference}' detected in flow block '{Name}'.",
                        FlowBloxLogLevel.Warning);
                }
            }
        }

        private void InputReference_IterationStart(BaseRuntime runtime)
        {
            _passedResults.Clear(runtime);
        }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        protected int InputDatasets_CurrentIndex { get; private set; }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        protected int InputDatasets_Count { get; private set; }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public List<FlowBlockOutDataset> InputDatasets { get; private set; }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        public FlowBlockOutDataset InputDataset_CurrentlyProcessing { get; private set; }

        [JsonIgnore()]
        [DeepCopierIgnore()]
        [FlowBloxUI(UISelectionButtons.None)]
        public FlowBlockFlags CurrentFlags { get; private set; }

        private void InputReference_IterationEnd(BaseRuntime runtime)
        {
            var selector = FlowBlockDatasetSelectorFactory.Create(_passedResults.GetResults(runtime), this.InputBehaviorAssignments);
            var results = selector.GetResults();
            FilterInputDatasets(ref results);

            InputDatasets = results;
            InputDatasets_CurrentIndex = 0;
            InputDatasets_Count = results.Count;

            OnBeforeInputProcessing?.Invoke();

            runtime.NotifyBeforeInputProcessing(
                flowBlock: this,
                inputDatasetsCount: InputDatasets_Count,
                inputBehaviours: BuildInputBehaviourSummary(),
                iterationContext: IterationContext);

            foreach (var dataset in results)
            {
                EnqueueIterationEndWorkItem(runtime, new InputDatasetWorkItem(
                    block: this,
                    dataset: dataset,
                    applyDatasetAndExecute: (rt, blk, dataset) =>
                    {
                        blk.InputDataset_CurrentlyProcessing = dataset;

                        foreach (var fieldValueMapping in dataset.FieldValueMappings)
                        {
                            fieldValueMapping.Field.SetValue(rt, fieldValueMapping.Value);
                            SetPrecedingFieldValues(rt, blk, fieldValueMapping.PrecedingFieldValues);
                        }
                        blk.InvokeExecutor(rt, blk._storedExecutor);
                        blk.InputDatasets_CurrentIndex++;
                    }
                ));
            }

            InputDataset_CurrentlyProcessing = null;
        }

        private IReadOnlyDictionary<string, Enum> BuildInputBehaviourSummary()
        {
            var behaviors = new Dictionary<string, Enum>(StringComparer.OrdinalIgnoreCase);

            foreach (var assignment in InputBehaviorAssignments.Where(x => x?.FlowBlock != null))
            {
                var flowBlockName = assignment.FlowBlock.Name;
                if (string.IsNullOrWhiteSpace(flowBlockName))
                    continue;

                behaviors[flowBlockName] = assignment.Behavior;
            }

            return behaviors;
        }

        private static void EnqueueIterationEndWorkItem(BaseRuntime runtime, IRuntimeWorkItem workItem)
        {
            var cache = IterationEndWorkItemCache.Value;
            if (cache == null)
                throw new InvalidOperationException("An iteration end work item cache must be initialized before runtime work items can be added.");

            cache.Add(workItem);
        }

        private void FilterInputDatasets(ref List<FlowBlockOutDataset> results)
        {
            var _alreadyProcessedDatasets = new HashSet<string>();

            if (this.InputIgnoreDuplicates)
            {
                var filteredResults = new List<FlowBlockOutDataset>();

                foreach (var dataset in results)
                {
                    var datasetHash = string.Join('|', dataset.FieldValueMappings.Select(x => x.Value));
                    if (!_alreadyProcessedDatasets.Contains(datasetHash))
                    {
                        _alreadyProcessedDatasets.Add(datasetHash);
                        filteredResults.Add(dataset);
                    }
                }

                results = filteredResults;
            }
        }

        private void SetPrecedingFieldValues(BaseRuntime runtime, BaseFlowBlock baseFlowBlock, Dictionary<FieldElement, string> precedingFieldValues)
        {
            foreach(var referencedFlowBlock in baseFlowBlock.ReferencedFlowBlocks.OfType<BaseResultFlowBlock>())
            {
                if (referencedFlowBlock == IterationContext)
                    break;

                foreach(var field in referencedFlowBlock.Fields)
                {
                    if (precedingFieldValues.TryGetValue(field, out var value))
                        field.SetValueWithoutEvaluation(runtime, value);
                }
            }
        }

        protected void SetFieldRequirements(IEnumerable<FieldRequiredDefinitionBase> definitions)
        {
            foreach (var definition in definitions)
            {
                if (definition.Field != null)
                    SetFieldRequirement(definition.Field, definition.IsRequired);
            }
        }

        private void SynchronizeFieldRequiredDefinitions()
        {
            var definitions = FlowBlockObjectCollector.CollectObjects<FieldRequiredDefinitionBase>(
                this,
                new HashSet<object>(),
                recursive: false,
                excludedTypes: [typeof(ManagedObject), typeof(BaseFlowBlock)]);

            SetFieldRequirements(definitions);
        }

        public override void OnAfterSave()
        {
            base.OnAfterSave();
            SynchronizeFieldRequiredDefinitions();
            this.PropertyValuesChanged();
        }

        public override List<string> GetDisplayableProperties() => base.GetDisplayableProperties();

        public override string ToString() => this.Name;
    }
}
