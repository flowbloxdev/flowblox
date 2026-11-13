using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Models.Base;
using System.Globalization;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Parsing;
using FlowBlox.Core.Util.Fields;
using Newtonsoft.Json;

namespace FlowBlox.Core.Models.Components
{
    [Display(Name = "TypeElement_DisplayName", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    public class TypeElement : FlowBloxReactiveObject
    {
        public TypeElement()
        {
            var culture = CultureInfo.CurrentCulture;
            DecimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
            GroupSeparator = culture.NumberFormat.NumberGroupSeparator;
            DateFormat = GetDefaultDateTimeFormat(culture);
            IsNullable = true;
        }

        private static string GetDefaultDateTimeFormat(CultureInfo culture)
        {
            if (culture == null)
                culture = CultureInfo.InvariantCulture;

            var datePart = culture.DateTimeFormat.ShortDatePattern?.Trim();
            var timePart = culture.DateTimeFormat.LongTimePattern?.Trim();

            if (string.IsNullOrWhiteSpace(datePart) && string.IsNullOrWhiteSpace(timePart))
                return "o";

            if (string.IsNullOrWhiteSpace(datePart))
                return timePart;

            if (string.IsNullOrWhiteSpace(timePart))
                return datePart;

            return $"{datePart} {timePart}";
        }

        [Required]
        [Display(Name = "TypeElement_FieldType", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UISelectionButtons.None)]
        public FieldTypes FieldType { get; set; }

        [ActivationCondition(MemberName = nameof(FieldType), Value = FieldTypes.DateTime)]
        [ConditionallyRequired]
        [Display(Name = "TypeElement_DateFormat", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UISelectionButtons.None, ToolboxCategory = nameof(FlowBloxToolboxCategory.Format))]
        public string DateFormat { get; set; }

        [ActivationCondition(MemberName = nameof(FieldType), Values = new object[] { FieldTypes.Float, FieldTypes.Double })]
        [ConditionallyRequired()]
        [Display(Name = "TypeElement_DecimalSeparator", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string DecimalSeparator { get; set; }

        [ActivationCondition(MemberName = nameof(FieldType), Values = new object[] { FieldTypes.Float, FieldTypes.Double })]
        [ConditionallyRequired()]
        [Display(Name = "TypeElement_GroupSeparator", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UISelectionButtons.None)]
        public string GroupSeparator { get; set; }

        [ActivationCondition(MemberName = nameof(HasNullableInfo), Value = true)]
        [DependsOnProperty(MemberName = nameof(FieldType))]
        [Display(Name = "TypeElement_IsNullable", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool IsNullable { get; set; }

        public FieldElement GetReferencedField()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetFieldElements()
                .FirstOrDefault(x => x.FieldType == this);
        }

        private bool IsFieldElementSet(out string value)
        {
            var fieldElement = GetReferencedField();
            value = FlowBloxFieldHelper.ReplaceFieldsInString(
                fieldElement?.StringValue,
                resolveVaultReferences: fieldElement?.IsUserInputField() == true);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (HasNullableInfo && !IsNullable)
                    throw new InvalidOperationException(
                        $"No value set for field of type '{FieldType}'. The field is marked as non-nullable.");

                return false;
            }

            return true;
        }

        [JsonIgnore]
        public bool? ValueBoolean
        {
            get
            {
                if (!IsFieldElementSet(out string value))
                    return default;

                if (FieldType != FieldTypes.Boolean)
                    return default;

                if (bool.TryParse(value.Trim(), out bool result))
                    return result;

                throw new FormatException($"Failed to convert value '{value}' to boolean.");
            }
        }

        public IValueParsingStrategy GetSpecialParsingStrategy()
        {
            switch (FieldType)
            {
                case FieldTypes.DateTime: return new DateTimeParsingStrategy(DateFormat);
                case FieldTypes.Float: return new FloatParsingStrategy(GetNumberFormat());
                case FieldTypes.Double: return new DoubleParsingStrategy(GetNumberFormat());
                default: return null;
            }
        }

        public NumberFormatInfo GetNumberFormat()
        {
            var referencedField = GetReferencedField();
            var fieldInfo = referencedField != null ? $" (for field '{referencedField}')" : string.Empty;

            if (string.IsNullOrWhiteSpace(DecimalSeparator))
                throw new InvalidOperationException($"Decimal separator is not defined{fieldInfo}.");

            if (string.IsNullOrWhiteSpace(GroupSeparator))
                throw new InvalidOperationException($"Group separator is not defined{fieldInfo}.");

            return new NumberFormatInfo
            {
                NumberDecimalSeparator = DecimalSeparator,
                NumberGroupSeparator = GroupSeparator
            };
        }

        [JsonIgnore]
        public int? ValueInteger
        {
            get
            {
                if (!IsFieldElementSet(out string value))
                    return default;

                if (FieldType != FieldTypes.Integer)
                    return default;

                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                    return result;

                throw new FormatException($"Failed to convert value '{value}' to integer.");
            }
        }

        [JsonIgnore]
        public long? ValueLong
        {
            get
            {
                if (!IsFieldElementSet(out string value))
                    return default;

                if (FieldType != FieldTypes.Long)
                    return default;

                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
                    return result;

                throw new FormatException($"Failed to convert value '{value}' to long.");
            }
        }

        [JsonIgnore]
        public float? ValueFloat
        {
            get
            {
                if (!IsFieldElementSet(out string value))
                    return default;

                if (FieldType != FieldTypes.Float)
                    return default;

                return new FloatParsingStrategy(GetNumberFormat()).Parse(value);
            }
        }

        [JsonIgnore]
        public double? ValueDouble
        {
            get
            {
                if (!IsFieldElementSet(out string value))
                    return default;

                if (FieldType != FieldTypes.Double)
                    return default;

                return new DoubleParsingStrategy(GetNumberFormat()).Parse(value);
            }
        }

        [JsonIgnore]
        public DateTime? ValueDateTime
        {
            get
            {
                if (!IsFieldElementSet(out string value))
                    return default;

                if (FieldType != FieldTypes.DateTime)
                    return default;

                return new DateTimeParsingStrategy(DateFormat).Parse(value);
            }
        }

        [JsonIgnore]
        public byte[] ValueByteArray
        {
            get
            {
                if (!IsFieldElementSet(out string value))
                    return [];

                if (FieldType != FieldTypes.ByteArray)
                    return default;

                try
                {
                    return Convert.FromBase64String(value);
                }
                catch (FormatException)
                {
                    throw new FormatException($"Failed to convert value '{value}' to byte array (Base64 expected).");
                }
            }
        }

        [JsonIgnore]
        public bool HasNullableInfo
        {
            get
            {
                return
                    FieldType == FieldTypes.Integer ||
                    FieldType == FieldTypes.Long ||
                    FieldType == FieldTypes.Float ||
                    FieldType == FieldTypes.Double ||
                    FieldType == FieldTypes.Boolean ||
                    FieldType == FieldTypes.DateTime;
            }
        }

        public override string ToString()
        {
            var typeName = FieldType.GetDisplayName();
            string details = string.Empty;

            switch (FieldType)
            {
                case FieldTypes.DateTime:
                    details = $" (Format: {DateFormat ?? "n/a"})";
                    break;
                case FieldTypes.Double:
                case FieldTypes.Float:
                    details = $" (Decimal: '{DecimalSeparator ?? "n/a"}', Group: '{GroupSeparator ?? "n/a"}')";
                    break;
            }

            string nullableInfo = HasNullableInfo
                ? $" (Nullable: {IsNullable.ToString().ToLowerInvariant()})"
                : string.Empty;

            return $"{typeName}{details}{nullableInfo}";
        }
    }
}

