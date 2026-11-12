using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Parsing;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FlowBloxTest.FlowBlocks.Conditions
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class TypedComparisonTests
    {
        public static IEnumerable<object[]> TypedValues()
        {
            yield return [42, "42", "100"];
            yield return [3000000000L, "3000000000", "10000000000"];
            yield return [2.5f, "2.5", "10.5"];
            yield return [2.5d, "2.5", "10.5"];
            yield return [false, "false", "true"];
            yield return [new DateTime(2026, 10, 9), "2026-10-09", "2026-10-10"];
        }

        private static IValueParsingStrategy GetParsingStrategy(object value)
        {
            if (value is float) return new FloatParsingStrategy(CultureInfo.InvariantCulture.NumberFormat);
            if (value is double) return new DoubleParsingStrategy(CultureInfo.InvariantCulture.NumberFormat);
            if (value is DateTime) return new DateTimeParsingStrategy("yyyy-MM-dd");
            return null;
        }

        [TestMethod]
        [DynamicData(nameof(TypedValues))]
        public void TypedValues_UseTypedEqualityAndOrdering(object left, string equal, string greater)
        {
            var previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                var strategy = GetParsingStrategy(left);
                Assert.IsTrue(new ComparisonCondition { Operator = ComparisonOperator.Equals, Value = equal }.Compare(left, strategy));
                Assert.IsFalse(new ComparisonCondition { Operator = ComparisonOperator.NotEquals, Value = equal }.Compare(left, strategy));
                Assert.IsTrue(new ComparisonCondition { Operator = ComparisonOperator.LowerThan, Value = greater }.Compare(left, strategy));
                Assert.IsFalse(new ComparisonCondition { Operator = ComparisonOperator.GreaterThan, Value = greater }.Compare(left, strategy));
                Assert.IsTrue(new ComparisonCondition { Operator = ComparisonOperator.LowerThanOrEquals, Value = equal }.Compare(left, strategy));
                Assert.IsTrue(new ComparisonCondition { Operator = ComparisonOperator.GreaterThanOrEquals, Value = equal }.Compare(left, strategy));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [TestMethod]
        [DynamicData(nameof(TypedValues))]
        public void PresenceChecks_DoNotParseTheRightValue(object left, string equal, string greater)
        {
            Assert.IsTrue(new ComparisonCondition { Operator = ComparisonOperator.HasValue, Value = "invalid" }.Compare(left));
            Assert.IsFalse(new ComparisonCondition { Operator = ComparisonOperator.HasNoValue, Value = null }.Compare(left));
            Assert.IsFalse(new ComparisonCondition { Operator = ComparisonOperator.HasValue }.Compare((long?)null));
            Assert.IsTrue(new ComparisonCondition { Operator = ComparisonOperator.HasNoValue }.Compare((double?)null));
        }

        [TestMethod]
        [DynamicData(nameof(TypedValues))]
        public void InvalidTypedRightValue_ThrowsInsteadOfFallingBackToText(object left, string equal, string greater)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                new ComparisonCondition { Operator = ComparisonOperator.Equals, Value = "invalid" }.Compare(left, GetParsingStrategy(left)));
            Assert.ThrowsExactly<NotSupportedException>(() =>
                new ComparisonCondition { Operator = ComparisonOperator.Contains, Value = equal }.Compare(left));
        }

        [TestMethod]
        public void TypedComparison_ResolvesRuntimeFieldsBeforeConversion()
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var right = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, fieldName: "Right");
            right.StringValue = "10000000000";
            var condition = new ComparisonCondition { Operator = ComparisonOperator.LowerThan, Value = right.FullyQualifiedName };
            Assert.IsTrue(condition.Compare((long?)3000000000L));
        }

        [TestMethod]
        [DataRow("AQI=", true)]
        [DataRow("AgE=", false)]
        [DataRow("AQID", false)]
        public void ByteArrayComparison_UsesContentEquality(string right, bool equal)
        {
            Assert.AreEqual(equal, new ComparisonCondition { Operator = ComparisonOperator.Equals, Value = right }.Compare(new byte[] { 1, 2 }));
            Assert.AreEqual(!equal, new ComparisonCondition { Operator = ComparisonOperator.NotEquals, Value = right }.Compare(new byte[] { 1, 2 }));
        }

        [TestMethod]
        [DataRow(null, null, true)]
        [DataRow(null, "", true)]
        [DataRow("", null, true)]
        [DataRow("", "", true)]
        [DataRow("", " \t", true)]
        [DataRow("", "AQI=", false)]
        [DataRow("AQI=", null, false)]
        [DataRow("AQI=", "AQI=", true)]
        [DataRow("AQI=", "AgE=", false)]
        public void BinaryFieldConditions_ResolveReferencesAndTreatEmptyAsMissing(string? left, string? right, bool equal)
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var first = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.ByteArray, "First");
            first.StringValue = left;
            var second = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.ByteArray, "Second");
            second.StringValue = right;
            var condition = new FieldComparisonCondition { FieldElement = first, Operator = ComparisonOperator.Equals, Value = second.FullyQualifiedName };
            var logical = new FieldLogicalComparisonCondition { FieldElement = first, Operator = ComparisonOperator.Equals, Value = second.FullyQualifiedName };

            Assert.AreEqual(equal, condition.Compare());
            Assert.AreEqual(equal, logical.Check());
            condition.Operator = ComparisonOperator.NotEquals;
            logical.Operator = ComparisonOperator.NotEquals;
            Assert.AreEqual(!equal, condition.Compare());
            Assert.AreEqual(!equal, logical.Check());
        }

        [TestMethod]
        [DataRow(null, true)]
        [DataRow("", true)]
        [DataRow(" \t", true)]
        [DataRow("AQI=", false)]
        public void MissingBinaryField_EqualsOnlyMissingLiteralValues(string? right, bool equal)
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.ByteArray, "Input");
            field.StringValue = null;
            var condition = new FieldComparisonCondition { FieldElement = field, Operator = ComparisonOperator.Equals, Value = right };
            Assert.AreEqual(equal, condition.Compare());
            condition.Operator = ComparisonOperator.NotEquals;
            Assert.AreEqual(!equal, condition.Compare());
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" \t")]
        public void EmptyByteArray_EqualsMissingLiteralValues(string? right)
        {
            Assert.IsTrue(new ComparisonCondition { Operator = ComparisonOperator.Equals, Value = right }.Compare(Array.Empty<byte>()));
            Assert.IsFalse(new ComparisonCondition { Operator = ComparisonOperator.NotEquals, Value = right }.Compare(Array.Empty<byte>()));
        }

        [TestMethod]
        [DataRow(null, false)]
        [DataRow("", false)]
        [DataRow(" \t", false)]
        [DataRow("AQI=", true)]
        public void BinaryPresence_TreatsNullAndEmptyAsMissing(string? content, bool present)
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.ByteArray, "Input");
            field.StringValue = content;
            var condition = new FieldComparisonCondition { FieldElement = field, Operator = ComparisonOperator.HasValue };
            Assert.AreEqual(present, condition.Compare());
            condition.Operator = ComparisonOperator.HasNoValue;
            Assert.AreEqual(!present, condition.Compare());
        }

        [TestMethod]
        public void ByteArrayComparison_RejectsInvalidBase64()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                new ComparisonCondition { Operator = ComparisonOperator.Equals, Value = "invalid" }.Compare(new byte[] { 1, 2 }));
        }

        [TestMethod]
        [DataRow(ComparisonOperator.Contains)]
        [DataRow(ComparisonOperator.GreaterThan)]
        public void ByteArrayComparison_RejectsUnsupportedOperators(ComparisonOperator op)
        {
            Assert.ThrowsExactly<NotSupportedException>(() =>
                new ComparisonCondition { Operator = op, Value = "AQI=" }.Compare(new byte[] { 1, 2 }));
        }

        [TestMethod]
        [DataRow(FieldTypes.ByteArray, false, true)]
        [DataRow(FieldTypes.ByteArray, true, true)]
        [DataRow(FieldTypes.Long, false, true)]
        [DataRow(FieldTypes.Long, true, true)]
        public void FieldValidation_AcceptsEqualityForComparableTypes(FieldTypes type, bool logical, bool expected)
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, type, "Input");
            field.StringValue = type == FieldTypes.ByteArray ? "AQI=" : "3000000000";
            object condition = logical
                ? new FieldLogicalComparisonCondition { FieldElement = field }
                : new FieldComparisonCondition { FieldElement = field };
            var context = new ValidationContext(condition) { MemberName = nameof(FieldComparisonCondition.FieldElement) };

            Assert.AreEqual(expected, Validator.TryValidateProperty(field, context, new List<ValidationResult>()));
        }

        [TestMethod]
        [DataRow(ComparisonOperator.Contains)]
        [DataRow(ComparisonOperator.NotContains)]
        public void ContainsValidation_RejectsTypedFieldsEvenWhenEmpty_AndAllowsText(ComparisonOperator op)
        {
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            foreach (var type in new[] { FieldTypes.Integer, FieldTypes.Long, FieldTypes.Float, FieldTypes.Double, FieldTypes.Boolean, FieldTypes.DateTime, FieldTypes.ByteArray })
            {
                var field = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, type);
                var condition = new FieldComparisonCondition { FieldElement = field, Operator = op };
                var context = new ValidationContext(condition) { MemberName = nameof(FieldComparisonCondition.FieldElement) };
                var results = new List<ValidationResult>();
                Assert.IsFalse(Validator.TryValidateProperty(field, context, results), $"Empty {type} must not support {op}.");
                Assert.IsTrue(results[0].MemberNames.Contains(nameof(FieldComparisonCondition.FieldElement)));

                var logical = new FieldLogicalComparisonCondition { FieldElement = field, Operator = op };
                var logicalContext = new ValidationContext(logical) { MemberName = nameof(FieldLogicalComparisonCondition.FieldElement) };
                Assert.IsFalse(Validator.TryValidateProperty(field, logicalContext, new List<ValidationResult>()));

                condition.Operator = ComparisonOperator.Equals;
                Assert.IsTrue(Validator.TryValidateProperty(field, context, new List<ValidationResult>()));
            }

            var text = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text);
            foreach (var value in new[] { null, "", "text" })
            {
                text.StringValue = value;
                var condition = new FieldComparisonCondition { FieldElement = text, Operator = op };
                var context = new ValidationContext(condition) { MemberName = nameof(FieldComparisonCondition.FieldElement) };
                Assert.IsTrue(Validator.TryValidateProperty(text, context, new List<ValidationResult>()));
            }
        }
    }
}
