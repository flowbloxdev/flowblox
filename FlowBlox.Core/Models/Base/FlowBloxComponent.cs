using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Reflection;
using FlowBlox.Enums;
using FlowBlox.Core.Validations;

namespace FlowBlox.Core.Models.Base
{
    [FlowBloxUIGroup("Global_Groups_Requirements", 50)]
    public abstract class FlowBloxComponent : FlowBloxReactiveObject, IFlowBloxComponent
    {
        private string _name;

        public event EventHandler ComponentChanged;

        [Required()]
        [Display(Name = "PropertyNames_Name", ResourceType = typeof(FlowBloxTexts), Order = -1)]
        [CustomValidation(typeof(FlowBloxComponent), nameof(ValidateName))]
        [FlowBloxUI(UISelectionButtons.None)]
        public virtual string Name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public static ValidationResult ValidateName(string name, ValidationContext context) => FlowBloxComponentNameValidator.Validate(name, context);

        public virtual bool HandleRequirements => true;

        /// <summary>
        /// Rates the follow-up needed when deletion removes or invalidates a dependency.
        /// </summary>
        /// <param name="owner">The object that declares the affected property, possibly a nested reactive object.</param>
        /// <param name="property">The property whose reference or collection entry is affected.</param>
        /// <param name="impact">The operation performed on the dependency.</param>
        /// <param name="componentProperty">The top-level property on this component containing the affected owner.</param>
        public virtual DeletionReviewStatus GetDeletionReviewStatus(
            object owner, PropertyInfo property, DeletionImpact impact, PropertyInfo componentProperty)
            => GetDefaultDeletionReviewStatus(owner, property, impact);

        internal static DeletionReviewStatus GetDefaultDeletionReviewStatus(
            object owner, PropertyInfo property, DeletionImpact impact) => impact switch
        {
            DeletionImpact.CollectionEntryRemoved => DeletionReviewStatus.Information,
            DeletionImpact.FieldReferenceInvalidated => DeletionReviewStatus.ManualActionRequired,
            _ => ReactiveObjectRequiredStateChecker.IsRequired(owner, property)
                ? DeletionReviewStatus.ManualActionRequired
                : DeletionReviewStatus.ReviewRecommended
        };

        public bool ShouldSerializeRequiredFields() => HandleRequirements;

        [ActivationCondition(MemberName = nameof(HandleRequirements), Value = true)]
        [Display(Name = "FlowBloxComponent_RequiredFields", Description = "FlowBloxComponent_RequiredFields_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "Global_Groups_Requirements", Order = 0)]
        [FlowBloxFieldSelection(DefaultRequiredValue = false, HideRequiredCheckbox = true)]
        [FlowBloxListView(ColumnMemberNames = [nameof(FieldElement.FlowBlockName), nameof(FieldElement.Name)])]
        [FlowBloxAssociation(Operations = UIOperations.Link | UIOperations.Unlink)]
        [FlowBloxItemsSource(DisplayMember = nameof(FieldElement.FullyQualifiedName), Method = nameof(GetPossibleFieldElements))]
        public ObservableCollection<FieldElement> RequiredFields { get; set; } = new ObservableCollection<FieldElement>();

        public virtual List<FieldElement> GetPossibleFieldElements()
        {
            return FlowBloxFieldsResolver.GetFieldsOrderedByExecutionFlow();
        }

        public void SetFieldRequirement(FieldElement fieldElement, bool isRequired)
        {
            if (fieldElement == null)
                throw new ArgumentNullException(nameof(fieldElement));

            if (!IsLoaded)
                return;

            if (isRequired)
            {
                if (!RequiredFields.Contains(fieldElement))
                {
                    RequiredFields.Add(fieldElement);
                    OnPropertyChanged(nameof(RequiredFields));
                }
            }
            else
            {
                if (RequiredFields.Contains(fieldElement))
                {
                    RequiredFields.Remove(fieldElement);
                    OnPropertyChanged(nameof(RequiredFields));
                }
            }
        }

        protected void SetRequiredInputField(
            ref FieldElement storage,
            FieldElement value,
            string[] additionalNotifies = null,
            [CallerMemberName] string propertyName = null)
        {
            if (ReferenceEquals(storage, value))
                return;

            if (storage != null)
                SetFieldRequirement(storage, isRequired: false);

            storage = value;

            if (storage != null)
                SetFieldRequirement(storage, isRequired: true);

            OnPropertyChanged(propertyName);

            if (additionalNotifies != null)
                foreach (var n in additionalNotifies)
                    OnPropertyChanged(n);
        }

        [FlowBloxUI(UISelectionButtons.None)]
        public string Version { get; set; }

        [JsonIgnore]
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.component, 16);

        [JsonIgnore]
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.component, 32);

        private string GetComponentAssemblyVersion()
        {
            return GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
        }

        protected FlowBloxComponent()
        {
            Version = GetComponentAssemblyVersion();
        }

        public virtual void RegisterPropertyChangedEventHandlers()
        {
            foreach (var fieldElement in GetAssociatedFields().ExceptNull())
            {
                fieldElement.OnNameChanged -= OnReferencedFieldNameChanged;
                fieldElement.OnNameChanged += OnReferencedFieldNameChanged;
            }
        }

        /// <summary>
        /// Called when a referenced field has been renamed.
        /// </summary>
        /// <param name="field">
        /// The <see cref="FieldElement"/> instance representing the field whose name has changed.
        /// </param>
        /// <param name="oldFQFieldName">
        /// The fully qualified field name before the change.
        /// </param>
        /// <param name="newFQFieldName">
        /// The fully qualified field name after the change.
        /// </param>
        /// <remarks>
        /// This method is responsible for updating all string properties of this FlowBlock
        /// that reference the renamed field via field selection expressions.
        /// The default implementation automatically locates and updates all affected properties
        /// using reflection-based field collectors.  
        /// Override this method only if additional or specialized update logic is required.
        /// </remarks>
        protected virtual void OnReferencedFieldNameChanged(FieldElement field, string oldFQFieldName, string newFQFieldName)
        {
            foreach (var reactiveObject in GetNestedReactiveObjects().Prepend(this))
            {
                foreach (var selection in reactiveObject.PropertySelectedFieldMap.ToArray())
                {
                    if (string.Equals(selection.Value, oldFQFieldName, StringComparison.Ordinal))
                        reactiveObject.SetPropertySelectedFieldName(selection.Key, newFQFieldName);
                }
            }

            var reflectionData = FlowBlockObjectCollector.CollectStringPropertiesContainingFields(this);
            foreach (var (property, instance) in reflectionData)
            {

                var currentValue = (string)property.GetValue(instance);
                if (string.IsNullOrWhiteSpace(currentValue))
                    continue;

                string replacedValue;
                replacedValue = FlowBloxFieldHelper.ReplaceFQName(currentValue, oldFQFieldName, newFQFieldName);

                // Only write back if something actually changed
                if (string.Equals(currentValue, replacedValue, StringComparison.Ordinal))
                    continue;

                property.SetValue(instance, replacedValue);
            }
        }

        /// <summary>
        /// Returns all <see cref="FieldElement"/> instances that are referenced through property definitions.
        /// This includes direct associations, field references in string-based properties with field selection enabled,
        /// and any nested field references within associated reactive objects.
        /// </summary>
        /// <returns>A list of associated <see cref="FieldElement"/> instances.</returns>
        public IEnumerable<FieldElement> GetAssociatedFields() => FlowBlockObjectCollector.CollectReferencedFieldElementsRecursive(this);

        /// <summary>
        /// Returns the associated managed objects that are connected via property definitions.
        /// </summary>
        public IEnumerable<IManagedObject> GetAssociatedManagedObjects() => FlowBlockObjectCollector.CollectManagedObjectsRecursive(this);

        /// <summary>
        /// Checks the installations or external components required to execute this component.
        /// The default implementation has no execution prerequisites.
        /// </summary>
        /// <remarks>
        /// The execution boundary evaluates the FlowBlock and its associated managed objects automatically
        /// before running the action. Override this method and read field-backed configuration with Resolve;
        /// do not call it from an Execute/Invoke delegate or DownloadCoreAsync implementation.
        /// Throw ExecutionPrerequisiteException to report actionable setup guidance. Do not create normal
        /// results, navigate successors, or change configuration here. Existing RuntimeStarted initialization
        /// keeps its original lifecycle; adopting this hook does not move initialization into execution.
        /// </remarks>
        /// <exception cref="FlowBlox.Core.Exceptions.ExecutionPrerequisiteException">
        /// A required installation is missing or an execution component cannot be initialized.
        /// </exception>
        public virtual void ValidateExecutionPrerequisites()
        {
        }

        /// <summary>
        /// Selects the components whose prerequisites are required for this execution.
        /// </summary>
        /// <remarks>
        /// The default includes this component and GetAssociatedManagedObjects recursively. Override only
        /// when an execution path does not use an associated component, such as reusing an existing file.
        /// Selection and validation run before the action; the shared evaluator de-duplicates by reference.
        /// </remarks>
        protected virtual IEnumerable<FlowBloxComponent> GetExecutionPrerequisiteComponents()
        {
            return GetAssociatedManagedObjects()
                .OfType<FlowBloxComponent>()
                .Prepend(this);
        }

        internal void EvaluateExecutionPrerequisites()
        {
            foreach (var component in GetExecutionPrerequisiteComponents()
                .Distinct<FlowBloxComponent>(ReferenceEqualityComparer.Instance))
            {
                component.ValidateExecutionPrerequisites();
            }
        }

        public virtual List<string> GetDisplayableProperties() => [nameof(Name)];

        public virtual void OnAfterCreate()
        {

        }

        /// <summary>
        /// Called after the component has been fully loaded and initialized.
        /// Override this method to perform additional initialization that depends on a fully constructed state.
        /// <para>
        /// When overriding this method, make sure to call <c>base.OnAfterLoad()</c> first,
        /// before executing any component-specific logic.
        /// </para>
        /// </summary>
        public override void OnAfterLoad()
        {
            this.RegisterPropertyChangedEventHandlers();
            base.OnAfterLoad();
        }

        /// <summary>
        /// Called for the transient component after an editable property transaction has been opened.
        /// </summary>
        public virtual void OnAfterOpen()
        {
        }

        public virtual void OnBeforeSave()
        {
            // Intentionally left blank.
            // Cleanup of unused required fields is handled by the UI save workflow,
            // where users can review and select references before removal.
        }

        public bool HasUnusedFieldElements(out List<FieldElement> unusedFieldElements)
        {
            unusedFieldElements = GetUnusedRequiredFieldElements();
            return unusedFieldElements.Count > 0;
        }

        public List<FieldElement> GetUnusedRequiredFieldElements()
        {
            var associatedFields = new HashSet<FieldElement>(GetAssociatedFields().ExceptNull());
            return RequiredFields
                .Where(required => required != null && !associatedFields.Contains(required))
                .Distinct()
                .ToList();
        }

        public void RemoveRequiredFields(IEnumerable<FieldElement> fieldsToRemove)
        {
            if (fieldsToRemove == null)
                return;

            var toRemoveSet = new HashSet<FieldElement>(fieldsToRemove.Where(x => x != null));
            if (toRemoveSet.Count == 0)
                return;

            var toRemove = RequiredFields.Where(toRemoveSet.Contains).ToList();
            foreach (var field in toRemove)
                RequiredFields.Remove(field);
        }

        public virtual void OnAfterSave()
        {
            this.RegisterPropertyChangedEventHandlers();
            NotifyComponentChanged();
        }

        public void NotifyComponentChanged() => ComponentChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Initializes option defaults for this component or FlowBlock.
        /// This overload also receives the currently stored options, allowing the implementation to choose defaults dynamically.
        /// </summary>
        /// <param name="defaults">A list to which default option definitions are added.</param>
        /// <param name="currentOptions">The existing configured options, enabling conditional initialization</param>
        public virtual void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions) => OptionsInit(defaults);


        /// <summary>
        /// Initializes default options for this component or FlowBlock.
        /// Derived classes should override this method to declare their configurable default settings. 
        /// These defaults can be modified by the user.
        /// </summary>
        /// <param name="defaults">A list to which default option definitions are added.</param>
        public virtual void OptionsInit(List<OptionElement> defaults)
        {
            // This method can be implemented in derived classes.
        }

        public virtual void RuntimeFinished(BaseRuntime runtime)
        {
            // This method can be implemented in derived classes.
        }

        public virtual void RuntimeStarted(BaseRuntime runtime)
        {
            // This method can be implemented in derived classes.
        }
    }
}
