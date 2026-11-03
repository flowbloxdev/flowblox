using FlowBlox.Core.Provider.Placeholders;
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
        private static readonly HashSet<Type> SupportedSimplePropertyTypes = new HashSet<Type>
        {
            typeof(int),
            typeof(long),
            typeof(float),
            typeof(double),
            typeof(bool),
            typeof(DateTime)
        };

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

        public static string ReplaceFieldsInString(string value)
        {
            if (value == null)
                return null;

            // Field elements
            var registry = FlowBloxRegistryProvider.GetRegistry();
            foreach (FieldElement fieldElement in registry?.GetFieldElements() ?? Enumerable.Empty<FieldElement>())
            {
                if (value.Contains(fieldElement.FullyQualifiedName))
                    value = value.Replace(fieldElement.FullyQualifiedName, fieldElement.StringValue ?? "");
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

            return value;
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

            var selectedFieldProperty = target.GetType().GetProperty(
                propertyInfo.Name + GlobalConstants.SimplePropertySelectedFieldSuffix);

            if (selectedFieldProperty == null)
                return directValue;

            if (selectedFieldProperty.GetValue(target) is not FieldElement selectedField)
                return directValue;

            var resolvedValue = ResolveSimpleFieldValue(typeof(TValue), selectedField.Value);
            return (TValue)resolvedValue;
        }

        private static object ResolveSimpleFieldValue(Type targetType, object fieldValue)
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
            if (!IsSupportedSimplePropertyType(propertyInfo.PropertyType) &&
                !EnumHelper.IsEnumType(propertyInfo))
                return directValue;

            var selectedFieldProperty = target.GetType().GetProperty(
                propertyInfo.Name + GlobalConstants.SimplePropertySelectedFieldSuffix);

            if (selectedFieldProperty?.GetValue(target) is FieldElement selectedField)
                return selectedField;

            return directValue;
        }

        private static bool IsSupportedSimplePropertyType(Type propertyType)
        {
            var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            return SupportedSimplePropertyTypes.Contains(underlyingType);
        }

    }
}
