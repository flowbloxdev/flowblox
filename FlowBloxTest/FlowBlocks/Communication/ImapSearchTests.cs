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

        // Keep the existing date-filter semantics shared with the simple search.
        Assert.AreEqual($"({expression}) UNSEEN SINCE 31-Oct-2026 BEFORE 18-Nov-2026", query);
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
