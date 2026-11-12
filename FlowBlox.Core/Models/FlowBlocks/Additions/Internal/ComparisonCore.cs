using FlowBlox.Core.Enums;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Parsing;
using System.Text.RegularExpressions;
using System.Globalization;
using FlowBlox.Core.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Xml;
using System.Xml.XPath;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider;

namespace FlowBlox.Core.Models.FlowBlocks.Additions
{
    internal sealed class ComparisonCore
    {
        private delegate bool ValueParser<T>(string value, out T result);

        private static readonly IReadOnlyDictionary<Type, Func<string, IValueParsingStrategy, object>> _converters =
            new Dictionary<Type, Func<string, IValueParsingStrategy, object>>
            {
                [typeof(int)] = (value, strategy) => Parse<int>(value, (string text, out int result) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)),
                [typeof(long)] = (value, strategy) => Parse<long>(value, (string text, out long result) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)),
                [typeof(float)] = (value, strategy) => ParseSpecial(value, typeof(float), strategy),
                [typeof(double)] = (value, strategy) => ParseSpecial(value, typeof(double), strategy),
                [typeof(bool)] = (value, strategy) => Parse<bool>(value, bool.TryParse),
                [typeof(DateTime)] = (value, strategy) => ParseSpecial(value, typeof(DateTime), strategy)
            };

        public bool Check(ComparisonOperator op, string rawRightValue, object leftValue, IValueParsingStrategy parsingStrategy = null)
        {
            if (leftValue is byte[] bytes)
                return CheckBinary(op, rawRightValue, bytes);

            if (op == ComparisonOperator.HasValue)
                return leftValue != null && (leftValue is not string text || !string.IsNullOrEmpty(text));

            if (op == ComparisonOperator.HasNoValue)
                return leftValue == null || leftValue is string text && string.IsNullOrEmpty(text);

            // Nullable value types box as their underlying type, or as null.
            if (leftValue != null && _converters.TryGetValue(leftValue.GetType(), out var converter))
                return CheckTyped(op, rawRightValue, (IComparable)leftValue, converter, parsingStrategy);

            return Check(op, rawRightValue, leftValue?.ToString());
        }

        public bool CheckField(ComparisonOperator op, string rawRightValue, FieldElement field)
        {
            if (field?.FieldType?.FieldType == FieldTypes.ByteArray)
            {
                var bytes = GetRuntimeValue(field.FullyQualifiedName) == null ? null : (byte[])field.Value;
                return CheckBinary(op, rawRightValue, bytes);
            }

            return Check(op, rawRightValue, field?.Value, field?.FieldType?.GetSpecialParsingStrategy());
        }

        internal static bool SupportsBinaryOperator(ComparisonOperator op) =>
            op == ComparisonOperator.Equals ||
            op == ComparisonOperator.NotEquals ||
            op == ComparisonOperator.HasValue ||
            op == ComparisonOperator.HasNoValue;

        private static bool CheckBinary(ComparisonOperator op, string rawRightValue, byte[] left)
        {
            if (!SupportsBinaryOperator(op))
                throw new NotSupportedException("ByteArray values support equality and presence checks only.");

            var leftMissing = left == null || left.Length == 0;
            if (op == ComparisonOperator.HasValue)
                return !leftMissing;

            if (op == ComparisonOperator.HasNoValue)
                return leftMissing;

            var rightValue = GetRuntimeValue(rawRightValue);
            var rightMissing = string.IsNullOrWhiteSpace(rightValue);
            if (leftMissing || rightMissing)
            {
                var bothMissing = leftMissing && rightMissing;
                return op == ComparisonOperator.Equals ?
                    bothMissing :
                    !bothMissing;
            }

            byte[] right;
            try
            {
                right = Convert.FromBase64String(rightValue);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException("Invalid comparison value for ByteArray operation (Base64 expected).", exception);
            }

            var equal = left.AsSpan().SequenceEqual(right);
            return op == ComparisonOperator.Equals ? equal : !equal;
        }

        private static object ParseSpecial(string value, Type valueType, IValueParsingStrategy strategy)
        {
            if (strategy == null)
                throw new InvalidOperationException($"A parsing strategy is required for {valueType.Name} comparisons.");
            if (strategy.ValueType != valueType)
                throw new InvalidOperationException($"The parsing strategy for {strategy.ValueType.Name} cannot be used for {valueType.Name} comparisons.");
            try
            {
                return strategy.Parse(value);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException($"Invalid comparison value for {valueType.Name} operation.", exception);
            }
        }

        private static object Parse<T>(string value, ValueParser<T> parser)
        {
            if (!parser(value, out var result))
                throw new InvalidOperationException($"Invalid comparison value for {typeof(T).Name} operation.");

            return result;
        }

        public bool Check(ComparisonOperator op, string rawRightValue, string left)
        {
            string right = GetRuntimeValue(rawRightValue);

            switch (op)
            {
                case ComparisonOperator.HasValue: return !string.IsNullOrEmpty(left);
                case ComparisonOperator.HasNoValue: return string.IsNullOrEmpty(left);
                case ComparisonOperator.Equals:
                    return left == right || (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right));
                case ComparisonOperator.NotEquals:
                    return left != right && !(string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right));
                case ComparisonOperator.GreaterThan: return string.Compare(left, right) > 0;
                case ComparisonOperator.LowerThan: return string.Compare(left, right) < 0;
                case ComparisonOperator.GreaterThanOrEquals: return string.Compare(left, right) >= 0;
                case ComparisonOperator.LowerThanOrEquals: return string.Compare(left, right) <= 0;
            }

            if (right == null)
                return false;

            switch (op)
            {
                case ComparisonOperator.Contains:
                    return left != null && left.Contains(right);
                case ComparisonOperator.NotContains:
                    return left == null || !left.Contains(right);
            }

            if (left == null)
                return false;

            switch (op)
            {
                case ComparisonOperator.RegexIsTrue:
                    return Regex.IsMatch(left, right);
                case ComparisonOperator.RegexIsFalse:
                    return !Regex.IsMatch(left, right);
                case ComparisonOperator.JPathIsTrue:
                case ComparisonOperator.JPathIsFalse:
                    return CheckJPath(op, left, right);
                case ComparisonOperator.XPathIsTrue:
                case ComparisonOperator.XPathIsFalse:
                    return CheckXPath(op, left, right);
                default:
                    throw new NotSupportedException("This operator is not supported.");
            }
        }

        private static bool CheckJPath(ComparisonOperator op, string content, string expression)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(expression))
                    throw new JsonException("The JPath expression is empty.");

                var hasResults = JToken.Parse(content).SelectTokens(expression).Any();
                return op == ComparisonOperator.JPathIsTrue ? hasResults : !hasResults;
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                FlowBloxLogManager.Instance.GetLogger().Warn($"Could not evaluate {op}; the condition returns false.", exception);
                return false;
            }
        }

        private static bool CheckXPath(ComparisonOperator op, string content, string expression)
        {
            try
            {
                var document = new XmlDocument 
                { 
                    XmlResolver = null 
                };
                using var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                document.Load(reader);
                var hasResults = document.SelectSingleNode(expression) != null;
                return op == ComparisonOperator.XPathIsTrue ? hasResults : !hasResults;
            }
            catch (Exception exception) when (exception is XmlException or XPathException or ArgumentException)
            {
                FlowBloxLogManager.Instance.GetLogger().Warn($"Could not evaluate {op}; the condition returns false.", exception);
                return false;
            }
        }

        private bool CheckTyped(ComparisonOperator op, string rawRightValue, IComparable left, Func<string, IValueParsingStrategy, object> converter, IValueParsingStrategy parsingStrategy)
        {
            if (op != ComparisonOperator.Equals &&
                op != ComparisonOperator.NotEquals &&
                op != ComparisonOperator.GreaterThan &&
                op != ComparisonOperator.LowerThan &&
                op != ComparisonOperator.GreaterThanOrEquals &&
                op != ComparisonOperator.LowerThanOrEquals)
                throw new NotSupportedException($"This operator is not supported for {left.GetType().Name}.");

            var right = converter(GetRuntimeValue(rawRightValue), parsingStrategy);
            var comparison = left.CompareTo(right);

            switch (op)
            {
                case ComparisonOperator.Equals: return comparison == 0;
                case ComparisonOperator.NotEquals: return comparison != 0;
                case ComparisonOperator.GreaterThan: return comparison > 0;
                case ComparisonOperator.LowerThan: return comparison < 0;
                case ComparisonOperator.GreaterThanOrEquals: return comparison >= 0;
                case ComparisonOperator.LowerThanOrEquals: return comparison <= 0;
                default:
                    throw new NotSupportedException($"This operator is not supported for {left.GetType().Name}.");
            }
        }

        private static string GetRuntimeValue(string value)
        {
            var field = FlowBloxRegistryProvider.GetRegistry()?.GetFieldElementOrNull(value);
            return field != null
                ? FlowBloxFieldHelper.ResolveFieldValue(field)
                : FlowBloxFieldHelper.ReplaceFieldsInString(value);
        }
    }
}
