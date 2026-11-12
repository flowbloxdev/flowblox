using FlowBlox.Core.Provider.Placeholders;
using FlowBlox.Core.Models.Base;
using System.Globalization;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Provider.Project;
using System.Reflection;

namespace FlowBlox.Core.Util.Fields
{
    public static class FlowBloxFieldHelper
    {
        private static readonly Regex FieldReferencePattern = new(@"\$[A-Za-z0-9öäüÖÄÜ_-]+::[A-Za-z0-9öäüÖÄÜ_.-]+", RegexOptions.Compiled);

        /// <summary>
        /// Finds possible unresolved field references for warnings only.
        /// Matches may be intentional literal values and must never block validation or execution.
        /// </summary>
        public static IEnumerable<string> GetUnresolvedFieldReferences(string value)
        {
            return FieldReferencePattern.Matches(value ?? string.Empty)
                .Select(match => match.Value)
                .Distinct();
        }
            

        public static string ResolveFieldValue(FieldElement field)
        {
            ArgumentNullException.ThrowIfNull(field);

            return ReplaceFieldsInString(field.StringValue, resolveVaultReferences: field.IsUserInputField());
        }

        public static bool IsResolvedFieldValueEmpty(FieldElement field)
        {
            if (field == null)
                return true;

            var resolvedValue = ResolveFieldValue(field);

            return string.IsNullOrWhiteSpace(resolvedValue);
        }

        public static List<FieldElement> GetFieldElementsFromString(string value)
        {
            List<FieldElement> referencedFields = new List<FieldElement>();

            if (value == null)
                return referencedFields;

            Regex regex_GetFields = new Regex(BaseFlowBlock.Regex_FullyQualifiedFieldNames);
            MatchCollection matchCollection = regex_GetFields.Matches(value);
            foreach (Match match_Field in matchCollection)
            {
                FieldElement referencedField = FlowBloxRegistryProvider.GetRegistry().GetFieldElementOrNull(match_Field.Value);
                if (referencedField != null)
                    referencedFields.Add(referencedField);
            }
            return referencedFields;
        }

        public static Dictionary<string, string> ReplaceFieldsInDictionary(Dictionary<string, string> inputDictionary)
        {
            return inputDictionary.ToDictionary(
                x => ReplaceFieldsInString(x.Key),
                y => ReplaceFieldsInString(y.Value));
        }

        /// <summary>
        /// Resolves placeholders in arbitrary text. For configuration properties, prefer
        /// the null-safe Resolve extension so string and scalar reads follow the same convention.
        /// </summary>
        public static string ReplaceFieldsInString(string value) => ReplaceFieldsInString(value, resolveVaultReferences: true);

        /// <summary>
        /// Resolves arbitrary text and, when enabled, vault references after all other substitutions; preserves a null input.
        /// Use the Resolve extension for normal business configuration property access.
        /// </summary>
        public static string ReplaceFieldsInString(string value, bool resolveVaultReferences)
        {
            if (value == null)
                return null;

            // Field elements
            var registry = FlowBloxRegistryProvider.GetRegistry();
            foreach (FieldElement fieldElement in registry?.GetFieldElements() ?? Enumerable.Empty<FieldElement>())
            {
                if (!value.Contains(fieldElement.FullyQualifiedName))
                    continue;

                value = value.Replace(fieldElement.FullyQualifiedName, fieldElement.StringValue ?? string.Empty);
            }

            // Project property elements
            var activeProject = FlowBloxProjectManager.Instance.ActiveProject;
            if (activeProject != null)
            {
                var propertyElements = activeProject.GetProjectPropertyElements();
                foreach (var prop in propertyElements)
                {
                    var placeholder = prop.Placeholder;
                    if (value.Contains(placeholder))
                        value = value.Replace(placeholder, prop.Value ?? "");
                }

                var propertyValuesByKey = propertyElements
                    .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                foreach (var alias in activeProject.ProjectPropertyPlaceholderAliases)
                {
                    if (!propertyValuesByKey.TryGetValue(alias.Value, out var resolvedValue))
                        continue;

                    var aliasPlaceholder = FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Project, alias.Key);
                    if (value.Contains(aliasPlaceholder))
                        value = value.Replace(aliasPlaceholder, resolvedValue ?? string.Empty);
                }
            }

            // Options elements
            var optionsInstance = FlowBloxOptions.GetOptionInstance();
            foreach (var optionElement in optionsInstance.GetOptions().Where(x => x.IsPlaceholderEnabled))
            {
                string optionPlaceholder = FlowBloxSourceIds.GetPlaceholder(FlowBloxSourceId.Options, optionElement.Name);
                if (value.Contains(optionPlaceholder))
                    value = value.Replace(optionPlaceholder, optionElement.Value?.ToString() ?? "");
            };

            return resolveVaultReferences 
                ? Vault.FlowBloxVault.Resolve(value) 
                : value;
        }

        private static string GetParameterPrefixForDbType(DbTypes dbType)
        {
            switch (dbType)
            {
                case DbTypes.Oracle:
                    return ":";
                case DbTypes.MSSQL:
                case DbTypes.MySQL:
                case DbTypes.SQLite:
                    return "@";
                default:
                    throw new NotSupportedException($"DbType {dbType} is not supported.");
            }
        }

        public static string ReplaceFieldsInSQL(
            string sqlStatement,
            DbTypes dbType,
            out Dictionary<string, object> parameters,
            bool useFieldValuesAsParameters = true)
        {
            parameters = new Dictionary<string, object>();
            if (!useFieldValuesAsParameters)
                return ReplaceFieldsInString(sqlStatement);

            int index = 0;
            foreach (FieldElement fieldElement in FlowBloxRegistryProvider.GetRegistry().GetFieldElements())
            {
                if (sqlStatement.Contains(fieldElement.FullyQualifiedName))
                {
                    string parameterKey = GetParameterPrefixForDbType(dbType) + index.ToString();
                    parameters[parameterKey] = fieldElement.Value ?? DBNull.Value;
                    sqlStatement = sqlStatement.Replace(fieldElement.FullyQualifiedName, parameterKey);
                    index++;
                }
            }
            return sqlStatement;
        }
        public static string ReplaceFQName(string value, string fqOld, string fqNew)
        {
            if (!string.IsNullOrEmpty(value) && value.Contains(fqOld))
                value = value.Replace(fqOld, fqNew);
            return value;
        }
        /// <summary>
        /// Resolves a scalar selection or returns its configured literal without mutating it.
        /// Low-level implementation for Resolve; prefer that extension at configuration consumption sites,
        /// including inside FlowBlock invocations, modifiers and initialization callbacks.
        /// </summary>
        public static TValue GetSimplePropertyOrFieldValue<TTarget, TValue>(
            TTarget target,
            Expression<Func<TTarget, TValue>> expression)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (expression?.Body is not MemberExpression memberExpression)
                throw new ArgumentException("Expression must reference a property.", nameof(expression));

            if (memberExpression.Member is not System.Reflection.PropertyInfo propertyInfo)
                throw new ArgumentException("Expression must reference a property.", nameof(expression));

            var directValue = (TValue)propertyInfo.GetValue(target);

            if (GetPropertyValueOrSelectedField(target, propertyInfo) is not FieldElement selectedField)
                return directValue;

            var resolvedValue = ResolveSimpleFieldValue(typeof(TValue), selectedField.Value);
            return (TValue)resolvedValue;
        }

        internal static object ResolveSimpleFieldValue(Type targetType, object fieldValue)
        {
            var underlyingTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var targetIsNullable = Nullable.GetUnderlyingType(targetType) != null || !underlyingTargetType.IsValueType;

            if (fieldValue == null)
            {
                if (targetIsNullable)
                    return null;

                return Activator.CreateInstance(underlyingTargetType);
            }

            if (underlyingTargetType.IsInstanceOfType(fieldValue))
                return fieldValue;

            if (underlyingTargetType.IsEnum)
            {
                if (Enum.TryParse(underlyingTargetType, Convert.ToString(fieldValue, CultureInfo.InvariantCulture), true, out var parsedEnum))
                    return parsedEnum;

                return targetIsNullable ? null : Activator.CreateInstance(underlyingTargetType);
            }

            if (underlyingTargetType == typeof(bool))
            {
                if (fieldValue is string boolText && bool.TryParse(boolText, out var boolParsed))
                    return boolParsed;

                return Convert.ToBoolean(fieldValue, CultureInfo.InvariantCulture);
            }

            if (underlyingTargetType == typeof(int))
            {
                if (fieldValue is string intText && int.TryParse(intText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intParsed))
                    return intParsed;

                return Convert.ToInt32(fieldValue, CultureInfo.InvariantCulture);
            }

            if (underlyingTargetType == typeof(long))
            {
                if (fieldValue is string longText && long.TryParse(longText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longParsed))
                    return longParsed;

                return Convert.ToInt64(fieldValue, CultureInfo.InvariantCulture);
            }

            if (underlyingTargetType == typeof(float))
            {
                if (fieldValue is string floatText && float.TryParse(floatText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var floatParsed))
                    return floatParsed;

                return Convert.ToSingle(fieldValue, CultureInfo.InvariantCulture);
            }

            if (underlyingTargetType == typeof(double))
            {
                if (fieldValue is string doubleText && double.TryParse(doubleText, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleParsed))
                    return doubleParsed;

                return Convert.ToDouble(fieldValue, CultureInfo.InvariantCulture);
            }

            if (underlyingTargetType == typeof(DateTime))
            {
                if (fieldValue is string dateText && DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedDate))
                    return parsedDate;

                if (fieldValue is DateTimeOffset dto)
                    return dto.DateTime;

                return Convert.ToDateTime(fieldValue, CultureInfo.InvariantCulture);
            }

            throw new InvalidOperationException($"Type '{underlyingTargetType.Name}' is not supported for simple property field resolution.");
        }

        public static object GetPropertyValueOrSelectedField(object target, PropertyInfo propertyInfo)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (propertyInfo == null)
                throw new ArgumentNullException(nameof(propertyInfo));

            var directValue = propertyInfo.GetValue(target);
            if (!FlowBloxFieldSelection.Supports(propertyInfo) || !FlowBloxFieldSelection.SupportsScalar(propertyInfo.PropertyType))
                return directValue;

            if (target is FlowBloxReactiveObject reactiveObject)
                return reactiveObject.GetPropertySelectedField(propertyInfo.Name) ?? directValue;

            return directValue;
        }

    }
}
