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
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Models.Components
{
    [Display(Name = "FieldElement_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxUIGroup("Global_Groups_Default", 0, ControlAlignment.Top)]
    [FlowBloxUIGroup("FieldElement_Groups_Behavior", 5, ControlAlignment.Top)]
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

        public override void OnAfterCreate()
        {
            base.OnAfterCreate();

            if (_storeValueLocally == null && UserField)
            {
                if (UserFieldType == UserFieldTypes.Input)
                    StoreValueLocally = true;
            }

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
            var oldFieldName = GetDefaultFieldName(this.Source, this, oldSourceName, FieldNameGenerationMode.DeriveFromFlowBlock);
            
            if (this.Name == oldFieldName)
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
        [FlowBloxUI(Factory = UIFactory.Association, Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete)]
        public TypeElement FieldType { get; set; }

        public bool UserField { get; set; }

        public UserFieldTypes UserFieldType { get; set; }

        public bool IsRegularField() => !UserField;

        public bool IsUserInputField() => UserField && UserFieldType == UserFieldTypes.Input;

        private string _stringValue;

        public bool ShouldSerializeStringValue()
        {
            if (!UserField)
                return false;

            if (StoreValueLocally)
                return false;

            return true;
        }

        [ActivationCondition(MemberName = nameof(UserFieldType), Values = [ UserFieldTypes.Input, UserFieldTypes.Memory])]
        [Display(Name = "FieldElement_StringValue", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, UiOptions = UIOptions.EnableFieldSelection,
            SelectionFilterMethod = nameof(GetPossibleValues),
            SelectionFilterDependency = nameof(ListOfValues),
            SelectionDisplayMember = nameof(ValueItem.DisplayName))]
        [FlowBloxComboBox(DropDownModeMethod = nameof(GetValueDropDownMode), SelectionValueMember = nameof(ValueItem.Value))]
        [RequiredIf(nameof(IsRequiredAtRuntimeStartup), true)]
        [FlowBloxFieldSelection(DefaultRequiredValue = false, HideRequiredCheckbox = true, 
                        AllowedFieldSelectionModes = FieldSelectionModes.ProjectProperties | FieldSelectionModes.Options)]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true,
            IsPasswordMethod = nameof(ShouldMaskValue), SyntaxHighlightingMember = nameof(SyntaxHighlighting))]
        public string StringValue
        {
            get
            {
                var runtime = _threadLocalRuntime.Value;
                string value;
                lock (_runtimeFieldValueSync)
                {
                    if (runtime != null && _runtimeToFieldValue.TryGetValue(runtime, out value))
                        return value;
                }
                return _stringValue;
            }
            set
            {
                _stringValue = value;
            }
        }

        public string ShortStringValue => GetShortStringValue(this.StringValue);

        public bool ShouldMaskValue() => IsPassword
            && FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.UIPropertyViewMaskPasswordValues)?.GetValueBoolean() == true;

        public DropDownMode GetValueDropDownMode() => LoVType == LoVType.PossibleValues && ListOfValues?.Count > 0
            ? DropDownMode.SelectionOnly
            : DropDownMode.Suggestion;

        public IEnumerable<ValueItem> GetPossibleValues() => IsPassword
            ? Enumerable.Empty<ValueItem>()
            : ListOfValues ?? Enumerable.Empty<ValueItem>();

        public static string GetShortStringValue(string value) => TextHelper.ShortenString(value, 100, true);

        private bool? _storeValueLocally;

        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_StoreValueLocally", Description = "FieldElement_StoreValueLocally_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 3)]
        [FlowBloxUI(ReadOnlyMethod = nameof(IsStoreValueLocallyReadOnly))]
        public bool StoreValueLocally
        {
            get => IsPassword || (_storeValueLocally ?? false);
            set
            {
                _storeValueLocally = IsPassword ? true : value;
                OnPropertyChanged();
            }
        }

        public bool IsStoreValueLocallyReadOnly() => IsPassword;

        [ActivationCondition(MemberName = nameof(UserFieldType), Value = UserFieldTypes.Memory)]
        [Display(Name = "FieldElement_ClearOnRuntimeStart", Description = "FieldElement_ClearOnRuntimeStart_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 1)]
        [FlowBloxUI(ReadOnlyMethod = nameof(IsClearOnRuntimeStartReadOnly))]
        [DependsOnProperty(MemberName = nameof(IsRequiredAtRuntimeStartup))]
        public bool ClearOnRuntimeStart { get; set; }

        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_IsRequiredAtRuntimeStartup", Description = "FieldElement_IsRequiredAtRuntimeStartup_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 0)]
        [FlowBloxUI(ReadOnlyMethod = nameof(IsStartupRequirementReadOnly))]
        [DependsOnProperty(MemberNames = [nameof(ClearOnRuntimeStart), nameof(UserFieldType)])]
        public bool IsRequiredAtRuntimeStartup { get; set; }

        public bool IsClearOnRuntimeStartReadOnly() => IsRequiredAtRuntimeStartup && !ClearOnRuntimeStart;

        public bool IsStartupRequirementReadOnly() => UserFieldType == UserFieldTypes.Memory
            && ClearOnRuntimeStart && !IsRequiredAtRuntimeStartup;

        private bool _isPassword;

        [ActivationCondition(ActivationMethod = nameof(IsUserInputField))]
        [Display(Name = "FieldElement_UsePasswordChar", Description = "FieldElement_UsePasswordChar_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Behavior", Order = 2)]
        public bool IsPassword
        {
            get => _isPassword;
            set
            {
                if (_isPassword == value)
                    return;

                _isPassword = value;

                if (value)
                {
                    _storeValueLocally = true;
                    OnPropertyChanged(nameof(StoreValueLocally));
                }

                OnPropertyChanged();
            }
        }

        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_SyntaxHighlighting", Description = "FieldElement_SyntaxHighlighting_Tooltip",
            ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(Factory = UIFactory.ComboBox, SelectionFilterMethod = nameof(GetPossibleSyntaxHighlightings))]
        [FlowBloxComboBox(DropDownMode = DropDownMode.SelectionOnly)]
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
                    _ => StringValue
                };
            }
        }


        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_LoVType", Description = "FieldElement_LoVType_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        public LoVType LoVType { get; set; } = LoVType.Suggestion;

        [ActivationCondition(MemberName = nameof(UserField), Value = true)]
        [Display(Name = "FieldElement_ListOfValues", ResourceType = typeof(FlowBloxTexts), Order = 6)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
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
        [FlowBloxUI(Factory = UIFactory.GridView, DisplayLabel = true)]
        [FlowBloxDataGrid(
            GridColumnMemberNames = new[]
            {
                nameof(ComparisonCondition.Operator),
                nameof(ComparisonCondition.Value)
            })]
        public ObservableCollection<ComparisonCondition> Conditions { get; set; }

        [ActivationCondition(ActivationMethod = nameof(IsRegularField))]
        [Display(Name = "FieldElement_Modifiers", Description = "FieldElement_Modifiers_Tooltip", ResourceType = typeof(FlowBloxTexts), GroupName = "FieldElement_Groups_Modifiers", Order = 0)]
        [FlowBloxUI(Factory = UIFactory.ListView, Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete, DisplayLabel = true)]
        [FlowBloxListView(IsMovable = true, LVColumnMemberNames = new[] { nameof(ModifierBase.ObjectDisplayName) })]
        public ObservableCollection<ModifierBase> Modifiers { get; set; }

        public string FullyQualifiedName => GetFullyQualifiedName(Source, UserField, Name);

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

