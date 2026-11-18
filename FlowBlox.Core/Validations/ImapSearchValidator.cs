using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.FlowBlocks.Communication;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Validations;

public static class ImapSearchValidator
{
    public static ValidationResult? Validate(object? value, ValidationContext context)
    {
        var block = context.ObjectInstance as IMAPMailFetchFlowBlock;
        if (block == null || string.IsNullOrWhiteSpace(block.SearchText) || string.IsNullOrWhiteSpace(block.SearchExpression))
            return ValidationResult.Success;

        return new ValidationResult(
            FlowBloxResourceUtil.GetLocalizedString("ImapSearchValidator_ConflictingSearch", typeof(FlowBloxTexts)),
            [nameof(block.SearchText), nameof(block.SearchExpression)]);
    }
}
