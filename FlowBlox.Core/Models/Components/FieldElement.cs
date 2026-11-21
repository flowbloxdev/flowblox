using FlowBlox.Core.Extensions;
using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components.Modifier;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.Fields;
using Newtonsoft.Json;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Models.Components
{
    /// <summary>
    /// Represents a field in the current unified field model.
    /// </summary>
    /// <remarks>
    /// TODO: Split into RuntimeFieldElement, MemoryFieldElement and InputFieldElement.
    /// Preserve legacy JSON references, $User:: lookup across input/memory fields and automatic runtime-field creation.
    /// See <see href="../../../docs/field-element-subtypes.md">the subtype and migration concept</see>.
    /// </remarks>
    [Display(Name = "FieldElement_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxUIGroup("Global_Groups_Default", 0, ControlAlignment.Top)]
    [FlowBloxUIGroup("FieldElement_Groups_Behavior", 5, ControlAlignment.Top)]
    [FlowBloxUIGroup("FieldElement_Groups_Advanced", 30, ControlAlignment.Top)]
    [FlowBloxUIGroup("FieldElement_Groups_Conditions", 10, ControlAlignment.Fill)]
    [FlowBloxUIGroup("FieldElement_Groups_Modifiers", 20, ControlAlignment.Fill)]
    [Serializable()]
    public class FieldElement : ManagedObject
    {
        private const string FallbackFieldFormat = "Field{0}";

        private static readonly SKColor RegularFieldColor = new(15, 118, 110);
        private static readonly SKColor InputFieldColor = new(30, 136, 229);
        private static readonly SKColor MemoryFieldColor = new(124, 58, 237);

        private FieldNameGenerationMode? _nameGenerationMode;

        public FieldElement()
        {
            this.Index = null;
            this.Conditions = new ObservableCollection<ComparisonCondition>();
            this.Modifiers = new ObservableCollection<ModifierBase>();
        }

        public FieldElement(FieldTypes fieldType) : this()
        {
            this.FieldType = new TypeElement()
            {
                FieldType = fieldType
            };
        }

        public FieldElement(BaseResultFlowBlock resultFlowBlock, FieldTypes fieldType) : this(fieldType)
        {
            this.Source = resultFlowBlock;
        }

        public FieldElement(BaseResultFlowBlock resultFlowBlock) : this(resultFlowBlock, FieldTypes.Text)
        {

        }

        public FieldElement(
            BaseResultFlowBlock resultFlowBlock, 
            FieldNameGenerationMode nameGenerationMode, 
            FieldTypes fieldType) : this(resultFlowBlock, fieldType)
        {
            this._nameGenerationMode = nameGenerationMode;
        }

        public override bool HandleRequirements => false;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(GetIconSvg(), 16, GetIconColor());

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(GetIconSvg(), 32, GetIconColor());

        [JsonIgnore]
        [Display(Name = "FieldElement_ValueStatusIcon", Description = "FieldElement_ValueStatusIcon_Tooltip", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(UISelectionButtons.None, Visible = false)]
        [FlowBloxImageColumn(typeof(FlowBloxIcons), nameof(FlowBloxIcons.circle), IconSize = 8, Opacity = 0.75, Width = 24, ShowHeader = false, ShowToolTip = false)]
        public SKImage ValueStatusIcon => FieldValuePresentation.GetIcon(StringValue, IsUserInputField() && IsRequiredAtRuntimeStartup, Pending);

        [JsonIgnore]
        public string ValueStatusDescription => FieldValuePresentation.GetDescription(StringValue, IsUserInputField() && IsRequiredAtRuntimeStartup, Pending);

        protected override void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
            if (string.IsNullOrEmpty(propertyName)
                || propertyName == nameof(StringValue)
                || propertyName == nameof(DefaultValue)
                || propertyName == nameof(Pending)
                || propertyName == nameof(IsPassword)
                || propertyName == nameof(IsRequiredAtRuntimeStartup)
                || propertyName == nameof(UserFieldType)
                || propertyName == nameof(UserField))
            {
                base.OnPropertyChanged(nameof(ValueStatusIcon));
                base.OnPropertyChanged(nameof(ValueStatusDescription));
            }

            if (string.IsNullOrEmpty(propertyName)
                || propertyName == nameof(IsRequiredAtRuntimeStartup)
                || propertyName == nameof(UserFieldType)
                || propertyName == nameof(UserField))
            {
                base.OnPropertyChanged(nameof(Icon16));
                base.OnPropertyChanged(nameof(Icon32));
            }
        }

        public override void OnAfterCreate()
        {
            base.OnAfterCreate();

            if (this.Source != null)
            {
                if (_nameGenerationMode != null)
                    this.Name = GetDefaultFieldName(this.Source, this, nameGenerationMode: _nameGenerationMode.Value);
                else
                    this.Name = GetDefaultFieldName(this.Source, this);
            }
        }

        private static string GetDefaultFieldName(
            BaseResultFlowBlock source,
            FieldElement field = null,
            string flowBlockName = null,
            FieldNameGenerationMode nameGenerationMode = FieldNameGenerationMode.UseFallbackIndexOnly)
        {
            if (flowBlockName == null)
                flowBlockName = source.Name;

            if (nameGenerationMode == FieldNameGenerationMode.DeriveFromFlowBlock)
            {
                if (flowBlockName.IndexOf(source.NamePrefix) == 0)
                {
                    var nameSuffix = flowBlockName.Substring(source.NamePrefix.Length);
                    if (!string.IsNullOrEmpty(nameSuffix) && !Regex.IsMatch(nameSuffix, @"\d+$"))
                        return nameSuffix;
                }
            }

            // Fallback (either explicitly or by failed DeriveFromFlowBlock rule):
            int fieldIndex;
            if (field != null)
            {
                fieldIndex = source.Fields.IndexOf(field);
                if (fieldIndex < 0)
                    fieldIndex = source.Fields.Count;
            }
            else
            {
                fieldIndex = source.Fields.Count;
            }

            return string.Format(FallbackFieldFormat, fieldIndex);
        }

        private void Source_OnNameChanged(BaseFlowBlock flowBlock, string oldSourceName, string newSourceName)
        {
            var oldFieldName = this.Name;
            var oldDerivedFieldName = GetDefaultFieldName(this.Source, this, oldSourceName, FieldNameGenerationMode.DeriveFromFlowBlock);

            if (oldFieldName == oldDerivedFieldName)
            {
                var newFieldName = GetDefaultFieldName(this.Source, this, newSourceName, FieldNameGenerationMode.DeriveFromFlowBlock);
                this.Name = newFieldName;
            }

            string fQOld = GetFullyQualifiedName(oldSourceName, oldFieldName);
            string fQNew = GetFullyQualifiedName(newSourceName, this.Name);

            this.OnNameChanged?.Invoke(this, fQOld, fQNew);
        }

        public delegate void FieldElementNameChangedEventHandler(FieldElement field, string oldName, string newName);

        public delegate void FieldElementValueChangedEventHandler(FieldElement field, string oldValue, string newValue);

        public event FieldElementNameChangedEventHandler OnNameChanged;

        public event FieldElementValueChangedEventHandler OnValueChanged;

        private string _fieldName { get; set; }

        [ActivationCondition(ActivationMethod = nameof(IsRegularField))]
        [Display(Name = "Global_FlowBlock", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        public virtual string FlowBlockName => this.Source?.Name ?? string.Empty;

        [Required()]
        [Display(Name = "PropertyNames_Name", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [CustomValidation(typeof(FlowBloxComponent), nameof(ValidateName))]
        [FlowBloxUI(UISelectionButtons.None)]
        public override string Name
        {
            get
            {
                return _fieldName;
            }
            set
            {
                string oldFieldName = _fieldName;
                bool changed = oldFieldName != value;
                bool isNew = string.IsNullOrEmpty(oldFieldName);

                this._fieldName = value;

                if (!isNew && changed)
                {
                    string fQOld = GetFullyQualifiedName(Source, UserField, oldFieldName);
                    string fQNew = GetFullyQualifiedName(Source, UserField, value);

                    OnNameChanged?.Invoke(this, fQOld, fQNew);
                }

                OnPropertyChanged();
            }
        }

        [Display(Name = "FieldElement_FieldType", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public TypeElement FieldType { get; set; }

        [FlowBloxUI(UISelectionButtons.None)]
        public bool UserField { get; set; }

        [FlowBloxUI(UISelectionButtons.None)]
        public UserFieldTypes UserFieldType { get; set; }

        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_Index", Description = "FieldElement_Index_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 4)]
        [FlowBloxUI(UISelectionButtons.None)]
        public int? Index { get; set; }

        public bool IsRegularField() => !UserField;

        public bool IsUserInputField() => UserField && UserFieldType == UserFieldTypes.Input;

        private string _stringValue;

        public bool ShouldSerializeStringValue() => UserField && UserFieldType == UserFieldTypes.Memory;

        internal string LocalStringValue => _stringValue;

        [OnDeserialized]
        private void MigrateInputValue(StreamingContext context)
        {
            if (!IsUserInputField())
                return;

            // Legacy migration: old project StringValue becomes the input default, never a local override.
            // The removed StoreValueLocally JSON member is intentionally ignored; passwords have no default.
            _defaultValue = IsPassword ? null : _defaultValue ?? _stringValue;
            _stringValue = null;
        }

        [ActivationCondition(MemberName = nameof(UserFieldType), Values = [ UserFieldTypes.Input, UserFieldTypes.Memory])]
        [Display(Name = "FieldElement_StringValue", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxComboBox(DropDownModeMethod = nameof(GetValueDropDownMode), SelectionValueMember = nameof(ValueItem.Value))]
        [FlowBloxFieldSelection(DefaultRequiredValue = false, HideRequiredCheckbox = true, 
            AllowedFieldSelectionModes = FieldSelectionModes.ProjectProperties | FieldSelectionModes.Options | FieldSelectionModes.Vault,
            AllowedFieldSelectionModesMethod = nameof(GetValueFieldSelectionModes))]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true,
            IsPasswordMethod = nameof(ShouldMaskValue), SyntaxHighlightingMember = nameof(SyntaxHighlighting),
            DefaultValueMember = nameof(DefaultValue))]
        [FlowBloxItemsSource(
            Method = nameof(GetPossibleValues),
            Dependency = nameof(ListOfValues),
            DisplayMember = nameof(ValueItem.DisplayName))]
        public string StringValue
        {
            get
            {
                var runtime = _threadLocalRuntime.Value;
                string value;
                lock (_runtimeFieldValueSync)
                {
                    if (runtime != null && _runtimeToFieldValue.TryGetValue(runtime, out value))
                        return GetEffectiveStringValue(value);
                }
                return GetEffectiveStringValue(_stringValue);
            }
            set
            {
                _stringValue = IsUserInputField() && !IsPassword && value == DefaultValue
                    ? null
                    : value;
            }
        }

        private string GetEffectiveStringValue(string value) => IsUserInputField() && string.IsNullOrEmpty(value)
            ? DefaultValue
            : value;

        public string ShortStringValue => GetShortStringValue(this.StringValue);

        public FieldSelectionModes GetValueFieldSelectionModes() => FieldSelectionModes.ProjectProperties
            | FieldSelectionModes.Options
            | (IsUserInputField() ? FieldSelectionModes.Vault : FieldSelectionModes.Default);

        public bool ShouldMaskValue() => IsPassword;

        public DropDownMode GetValueDropDownMode() => LoVType == LoVType.PossibleValues && ListOfValues?.Count > 0
            ? DropDownMode.SelectionOnly
            : DropDownMode.Suggestion;

        public IEnumerable<ValueItem> GetPossibleValues() => IsPassword
            ? Enumerable.Empty<ValueItem>()
            : ListOfValues ?? Enumerable.Empty<ValueItem>();

        public static string GetShortStringValue(string value) => TextHelper.ShortenString(value, 100, true);

        private string _defaultValue;

        [ActivationCondition(ActivationMethod = nameof(DisplayDefaultValue))]
        [Display(Name = "FieldElement_DefaultValue", Description = "FieldElement_DefaultValue_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 3)]
        [FlowBloxUI(ReadOnlyMethod = nameof(IsDefaultValueReadOnly))]
        [FlowBloxFieldSelection(DefaultRequiredValue = false, HideRequiredCheckbox = true,
            AllowedFieldSelectionModes = FieldSelectionModes.ProjectProperties | FieldSelectionModes.Options | FieldSelectionModes.Vault,
            AllowedFieldSelectionModesMethod = nameof(GetValueFieldSelectionModes))]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true, SyntaxHighlightingMember = nameof(SyntaxHighlighting))]
        [CustomValidation(typeof(FieldElement), nameof(ValidateDefaultValue))]
        [DependsOnProperty(MemberNames = [nameof(IsPassword), nameof(LoVType), nameof(ListOfValues)])]
        public string DefaultValue
        {
            get => IsPassword ? null : _defaultValue;
            set
            {
                // Editor copies can write the effective StringValue before copying DefaultValue.
                if (IsUserInputField() && !IsPassword
                    && (_stringValue == _defaultValue || _stringValue == value))
                    _stringValue = null;

                _defaultValue = IsPassword ? null : value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StringValue));
            }
        }

        public bool ShouldSerializeDefaultValue() => IsUserInputField() && !IsPassword;

        public bool DisplayDefaultValue() => IsUserInputField();

        public bool IsDefaultValueReadOnly() => IsPassword;

        public static ValidationResult ValidateDefaultValue(string value, ValidationContext context)
        {
            var field = (FieldElement)context.ObjectInstance;
            if (field.IsUserInputField() && !string.IsNullOrEmpty(value)
                && field.LoVType == LoVType.PossibleValues && field.ListOfValues?.Count > 0
                && !field.ListOfValues.Any(item => string.Equals(item.Value, value, StringComparison.Ordinal)))
            {
                return new ValidationResult(
                    FlowBloxResourceUtil.GetLocalizedString("FieldElement_DefaultValueNotAllowed"),
                    [nameof(DefaultValue)]);
            }

            return ValidationResult.Success;
        }

        [ActivationCondition(MemberName = nameof(UserFieldType), Value = UserFieldTypes.Memory)]
        [Display(Name = "FieldElement_ClearOnRuntimeStart", Description = "FieldElement_ClearOnRuntimeStart_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 1)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnlyMethod = nameof(IsClearOnRuntimeStartReadOnly))]
        [DependsOnProperty(MemberName = nameof(IsRequiredAtRuntimeStartup))]
        public bool ClearOnRuntimeStart { get; set; }

        private bool _isRequiredAtRuntimeStartup;

        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_IsRequiredAtRuntimeStartup", Description = "FieldElement_IsRequiredAtRuntimeStartup_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 0)]
        [FlowBloxUI(UISelectionButtons.None, ReadOnlyMethod = nameof(IsStartupRequirementReadOnly))]
        [DependsOnProperty(MemberNames = [nameof(ClearOnRuntimeStart), nameof(UserFieldType)])]
        public bool IsRequiredAtRuntimeStartup
        {
            get => _isRequiredAtRuntimeStartup;
            set
            {
                if (_isRequiredAtRuntimeStartup == value)
                    return;

                _isRequiredAtRuntimeStartup = value;
                OnPropertyChanged();
            }
        }

        public bool IsClearOnRuntimeStartReadOnly() => IsRequiredAtRuntimeStartup && !ClearOnRuntimeStart;

        public bool IsStartupRequirementReadOnly() => UserFieldType == UserFieldTypes.Memory
            && ClearOnRuntimeStart && !IsRequiredAtRuntimeStartup;

        private bool _isPassword;

        [ActivationCondition(ActivationMethod = nameof(IsUserInputField))]
        [Display(Name = "FieldElement_UsePasswordChar", Description = "FieldElement_UsePasswordChar_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 2)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool IsPassword
        {
            get => _isPassword;
            set
            {
                if (_isPassword == value)
                    return;

                _isPassword = value;

                if (value)
                    _defaultValue = null;

                OnPropertyChanged();
                OnPropertyChanged(nameof(DefaultValue));
                OnPropertyChanged(nameof(StringValue));
            }
        }

        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_SyntaxHighlighting", Description = "FieldElement_SyntaxHighlighting_Tooltip",
            ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Advanced", Order = 0)]
        [FlowBloxComboBox(DropDownMode = DropDownMode.SelectionOnly)]
        [FlowBloxItemsSource(Method = nameof(GetPossibleSyntaxHighlightings))]
        [FlowBloxUI(UISelectionButtons.None)]
        public string? SyntaxHighlighting { get; set; } = "Text";

        public IEnumerable<string> GetPossibleSyntaxHighlightings() =>
            ["Text", "JQL", "SQL", "JSON", "XML", "HTML", "JavaScript", "C#"];

        public Type GetConfiguredType()
        {
            switch (this.FieldType?.FieldType)
            {
                case FieldTypes.Text:
                    return typeof(string);
                case FieldTypes.Integer:
                    return typeof(int);
                case FieldTypes.DateTime:
                    return typeof(DateTime);
                case FieldTypes.Double:
                    return typeof(double);
                case FieldTypes.Boolean:
                    return typeof(bool);
                case FieldTypes.ByteArray:
                    return typeof(byte[]);
            }

            return typeof(string);
        }

        public object Value
        {
            get
            {
                var fieldType = FieldType?.FieldType;

                return fieldType switch
                {
                    FieldTypes.Boolean => FieldType.ValueBoolean,
                    FieldTypes.Integer => FieldType.ValueInteger,
                    FieldTypes.Long => FieldType.ValueLong,
                    FieldTypes.Float => FieldType.ValueFloat,
                    FieldTypes.Double => FieldType.ValueDouble,
                    FieldTypes.DateTime => FieldType.ValueDateTime,
                    FieldTypes.ByteArray => FieldType.ValueByteArray,
                    _ => FlowBloxFieldHelper.ReplaceFieldsInString(StringValue, resolveVaultReferences: IsUserInputField())
                };
            }
        }


        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_LoVType", Description = "FieldElement_LoVType_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(UISelectionButtons.None)]
        public LoVType LoVType { get; set; } = LoVType.Suggestion;

        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_ListOfValues", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        [FlowBloxDataGrid]
        public ObservableCollection<ValueItem> ListOfValues { get; set; }

        private BaseResultFlowBlock _source;

        public virtual BaseResultFlowBlock Source
        {
            get
            {
                return _source;
            }
            set
            {
                if (value != null)
                    value.OnNameChanged += Source_OnNameChanged;

                _source = value;
            }
        }

        [ActivationCondition(ActivationMethod = nameof(IsRegularField))]
        [Display(Name = "FieldElement_Conditions", Description = "FieldElement_Conditions_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Conditions", Order = 0)]
        [FlowBloxDataGrid(
            ColumnMemberNames = new[]
            {
                nameof(ComparisonCondition.Operator),
                nameof(ComparisonCondition.Value)
            })]
        public ObservableCollection<ComparisonCondition> Conditions { get; set; }

        [ActivationCondition(ActivationMethod = nameof(IsRegularField))]
        [Display(Name = "FieldElement_Modifiers", Description = "FieldElement_Modifiers_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Modifiers", Order = 0)]
        [FlowBloxListView(IsMovable = true, ColumnMemberNames = new[] { nameof(ModifierBase.ObjectDisplayName) })]
        [FlowBloxAssociation(Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public ObservableCollection<ModifierBase> Modifiers { get; set; }

        public string FullyQualifiedName => GetFullyQualifiedName(Source, UserField, Name);

        [FlowBloxUI(UISelectionButtons.None)]
        public bool Pending { get; internal set; }

        public static bool IsFullyQualifiedName(string name)
        {
            return (name.IndexOf("$") == 0) && name.Contains("::");
        }

        public static string GetSourceId(string fullyQualifiedFieldName)
        {
            return fullyQualifiedFieldName.Split("$:".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)[0];
        }

        public static string GetFieldName(string fullyQualifiedFieldName)
        {
            return fullyQualifiedFieldName.Split("$:".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)[1];
        }

        public static string GetFullyQualifiedName(BaseFlowBlock source, bool userField, string fieldName)
        {
            if (source == null)
            {
                if (!userField)
                    throw new Exception("The source object must not be null in field definition. FieldName=" + fieldName);
                else
                    return FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.User, fieldName);
            }

            return "$" + source.Name + "::" + fieldName;
        }

        public static string GetFullyQualifiedName(string elementName, string fieldName)
        {
            return "$" + elementName + "::" + fieldName;
        }

        private readonly Dictionary<BaseRuntime, string> _runtimeToFieldValue = new Dictionary<BaseRuntime, string>();
        private readonly object _runtimeFieldValueSync = new object();

        private ThreadLocal<BaseRuntime> _threadLocalRuntime = new ThreadLocal<BaseRuntime>(() => null);

        public void SetValueWithoutEvaluation(BaseRuntime runtime, string value, bool runtimeBound = false)
        {
            this.Pending = false;
            string oldValue = this.StringValue;

            if (runtimeBound)
            {
                lock (_runtimeFieldValueSync)
                    _runtimeToFieldValue[runtime] = value;

                _threadLocalRuntime.Value = runtime;
            }

            if (oldValue != value)
            {
                if (!runtimeBound)
                    this.StringValue = value;

                OnValueChanged?.Invoke(this, oldValue, value);
                runtime.NotifyFieldChange(this, oldValue, value);
            }
        }

        internal void ReleaseRuntimeValue(BaseRuntime runtime)
        {
            if (runtime == null)
                return;

            lock (_runtimeFieldValueSync)
                _runtimeToFieldValue.Remove(runtime);

            if (ReferenceEquals(_threadLocalRuntime.Value, runtime))
                _threadLocalRuntime.Value = null;
        }

        internal void ReleaseAllRuntimeValues()
        {
            lock (_runtimeFieldValueSync)
                _runtimeToFieldValue.Clear();

            _threadLocalRuntime.Value = null;
        }

        public void SetValue(BaseRuntime runtime, string value, bool runtimeBound = false)
        {
            if (value != this.StringValue)
            {
                var externalValue = GetExternalValue(value);
                if (!string.IsNullOrEmpty(value))
                    runtime.Report($"Field value changed: " + this.FullyQualifiedName + "=" + GetShortStringValue(externalValue));
                else
                    runtime.Report($"Field value changed: " + this.FullyQualifiedName + "=" + (IsPassword ? GlobalConstants.HiddenSensitiveValue : "<null>"));
            }
            SetValueWithoutEvaluation(runtime, value, runtimeBound);
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            if (IsRegularField() || (UserField && UserFieldType == UserFieldTypes.Memory && ClearOnRuntimeStart))
                this.SetValueWithoutEvaluation(runtime, string.Empty);
            base.RuntimeStarted(runtime);
        }

        public override string ToString() => this.FullyQualifiedName;

        private byte[] GetIconSvg()
        {
            if (!UserField)
                return FlowBloxIcons.variable;

            return UserFieldType switch
            {
                UserFieldTypes.Memory => FlowBloxIcons.memory,
                UserFieldTypes.Input => FlowBloxIcons.form_textbox,
                _ => FlowBloxIcons.variable
            };
        }

        private SKColor GetIconColor()
        {
            if (!UserField)
                return RegularFieldColor;

            return UserFieldType switch
            {
                UserFieldTypes.Memory => MemoryFieldColor,
                UserFieldTypes.Input => InputFieldColor,
                _ => RegularFieldColor
            };
        }

        public string GetExternalValue(string value)
        {
            if (!IsPassword)
                return value;

            return GlobalConstants.HiddenSensitiveValue;
        }
    }
}

