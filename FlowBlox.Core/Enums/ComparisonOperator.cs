using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;

namespace FlowBlox.Core.Enums
{
    public enum ComparisonOperator
    {
        [Display(Name = "ComparisonOperator_Equals", ResourceType = typeof(FlowBloxTexts))]
        Equals,

        [Display(Name = "ComparisonOperator_NotEquals", ResourceType = typeof(FlowBloxTexts))]
        NotEquals,

        [Display(Name = "ComparisonOperator_HasValue", ResourceType = typeof(FlowBloxTexts))]
        HasValue,

        [Display(Name = "ComparisonOperator_HasNoValue", ResourceType = typeof(FlowBloxTexts))]
        HasNoValue,

        [Display(Name = "ComparisonOperator_Contains", ResourceType = typeof(FlowBloxTexts))]
        Contains,

        [Display(Name = "ComparisonOperator_NotContains", ResourceType = typeof(FlowBloxTexts))]
        NotContains,

        [Display(Name = "ComparisonOperator_GreaterThan", ResourceType = typeof(FlowBloxTexts))]
        GreaterThan,

        [Display(Name = "ComparisonOperator_LowerThan", ResourceType = typeof(FlowBloxTexts))]
        LowerThan,

        [Display(Name = "ComparisonOperator_GreaterThanOrEquals", ResourceType = typeof(FlowBloxTexts))]
        GreaterThanOrEquals,

        [Display(Name = "ComparisonOperator_LowerThanOrEquals", ResourceType = typeof(FlowBloxTexts))]
        LowerThanOrEquals,

        [Display(Name = "ComparisonOperator_RegexIsTrue", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxEnumerationGroup("EnumerationGroup_AdvancedTextOperators", ResourceType = typeof(FlowBloxTexts),
            IconResourceType = typeof(FlowBloxIcons), IconResourceKey = nameof(FlowBloxIcons.text_box_search_outline), IconColor = "#546E7A")]
        RegexIsTrue,

        [Display(Name = "ComparisonOperator_RegexIsFalse", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxEnumerationGroup("EnumerationGroup_AdvancedTextOperators", ResourceType = typeof(FlowBloxTexts),
            IconResourceType = typeof(FlowBloxIcons), IconResourceKey = nameof(FlowBloxIcons.text_box_search_outline), IconColor = "#546E7A")]
        RegexIsFalse,

        [Display(Name = "ComparisonOperator_JPathIsTrue", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxEnumerationGroup("EnumerationGroup_AdvancedTextOperators", ResourceType = typeof(FlowBloxTexts),
            IconResourceType = typeof(FlowBloxIcons), IconResourceKey = nameof(FlowBloxIcons.text_box_search_outline), IconColor = "#546E7A")]
        JPathIsTrue,

        [Display(Name = "ComparisonOperator_JPathIsFalse", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxEnumerationGroup("EnumerationGroup_AdvancedTextOperators", ResourceType = typeof(FlowBloxTexts),
            IconResourceType = typeof(FlowBloxIcons), IconResourceKey = nameof(FlowBloxIcons.text_box_search_outline), IconColor = "#546E7A")]
        JPathIsFalse,

        [Display(Name = "ComparisonOperator_XPathIsTrue", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxEnumerationGroup("EnumerationGroup_AdvancedTextOperators", ResourceType = typeof(FlowBloxTexts),
            IconResourceType = typeof(FlowBloxIcons), IconResourceKey = nameof(FlowBloxIcons.text_box_search_outline), IconColor = "#546E7A")]
        XPathIsTrue,

        [Display(Name = "ComparisonOperator_XPathIsFalse", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxEnumerationGroup("EnumerationGroup_AdvancedTextOperators", ResourceType = typeof(FlowBloxTexts),
            IconResourceType = typeof(FlowBloxIcons), IconResourceKey = nameof(FlowBloxIcons.text_box_search_outline), IconColor = "#546E7A")]
        XPathIsFalse
    }
}
