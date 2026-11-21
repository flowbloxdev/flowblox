using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.FlowBlocks.Communication;

namespace FlowBloxTest.FlowBlocks.Communication;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ImapSearchTests
{
    [TestMethod]
    public void Expression_GroupsAllOrBranchesBeforeApplyingIndependentFilters()
    {
        const string expression = "OR TEXT \"invoice\" (OR TEXT \"meeting\" TEXT \"delivery\")";

        var query = IMAPMailFetchFlowBlock.BuildRawSearchQuery(
            new DateTime(2026, 11, 1),
            new DateTime(2026, 11, 17),
            expression,
            true);

        Assert.AreEqual($"({expression}) UNSEEN SINCE 1-Nov-2026 BEFORE 18-Nov-2026", query);
    }

    [TestMethod]
    public void StartDate_IncludesTheSelectedDayWithoutIncludingThePreviousYear()
    {
        var query = IMAPMailFetchFlowBlock.BuildRawSearchQuery(
            new DateTime(2025, 1, 1),
            null,
            "ALL",
            false);

        Assert.AreEqual("(ALL) SINCE 1-Jan-2025", query);
        Assert.AreEqual("(ALL) SINCE 1-Jan-0001", IMAPMailFetchFlowBlock.BuildRawSearchQuery(DateTime.MinValue, null, "ALL", false));
    }

    [TestMethod]
    public void Expression_WithoutAdditionalFilters_PreservesServerSpecificCriteria()
    {
        const string expression = "X-GM-RAW \"{invoice meeting} -label:spam\"";

        Assert.AreEqual($"({expression})", IMAPMailFetchFlowBlock.BuildRawSearchQuery(null, null, expression, false));
    }

    [TestMethod]
    public void AnnotationValidation_RejectsTwoSearchModesTogether()
    {
        var block = new IMAPMailFetchFlowBlock
        {
            SearchText = "invoice",
            SearchExpression = "TEXT \"meeting\""
        };
        var context = new ValidationContext(block) { MemberName = nameof(block.SearchExpression) };
        var errors = new List<ValidationResult>();

        Assert.IsFalse(Validator.TryValidateProperty(block.SearchExpression, context, errors));
        Assert.IsTrue(errors.Any(error => error.MemberNames.Contains(nameof(block.SearchText))));

        block.SearchText = null;
        errors.Clear();

        Assert.IsTrue(Validator.TryValidateProperty(block.SearchExpression, context, errors));
    }
}
