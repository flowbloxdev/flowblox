using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using System.Data;
using FlowBlox.Core.Attributes;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Format;
using System.Collections.ObjectModel;
using SkiaSharp;
using System.Text;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Extensions;
using System.Reflection;
using FlowBlox.Enums;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "FormatFlowBlock_DisplayName", Description = "FormatFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxSpecialExplanation("FormatFlowBlock_SpecialExplanation_CompositeFormatting", Icon = SpecialExplanationIcon.Information)]
    public class FormatFlowBlock : BaseSingleResultFlowBlock
    {
        public FormatFlowBlock()
        {
            FormatParameterDefinitions = new ObservableCollection<FormatParameterDefinition>();
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.format_line_style, 16, SKColors.ForestGreen);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.format_line_style, 32, SKColors.ForestGreen);


        [FlowBloxUI(UISelectionButtons.None)]
        public string FieldName { get; set; }


        [Display(
            Name = "FormatFlowBlock_AllowEmptyFormatExpression",
            Description = "FormatFlowBlock_AllowEmptyFormatExpression_Tooltip",
            ResourceType = typeof(FlowBloxTexts),
            Order = 0)]
        [FlowBloxUI(UISelectionButtons.None)]
        public bool AllowEmptyFormatExpression { get; set; } = false;

        [Display(Name = "FormatFlowBlock_FormatExpression", Description = "FormatFlowBlock_FormatExpression_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.CompositeFormat))]
        [FlowBloxTextBox(MultiLine = true, IsCodingMode = true)]
        [CustomValidation(typeof(FormatFlowBlock), nameof(ValidateFormatExpression))]
        [RequiredIf(nameof(AllowEmptyFormatExpression), false)]
        public string FormatExpression { get; set; }

        private ObservableCollection<FormatParameterDefinition> _formatParameterDefinitions;

        [Display(Name = "FormatFlowBlock_FormatParameters", Description = "FormatFlowBlock_FormatParameters_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxDataGrid]
        public ObservableCollection<FormatParameterDefinition> FormatParameterDefinitions
        {
            get
            {
                return _formatParameterDefinitions;
            }
            set
            {
                _formatParameterDefinitions = value;
            }
        }

        public static ValidationResult ValidateFormatExpression(string formatExpression, ValidationContext validationContext)
        {
            var block = (FormatFlowBlock)validationContext.ObjectInstance;
            formatExpression = FlowBloxFieldHelper.ReplaceFieldsInString(formatExpression);
            if (string.IsNullOrWhiteSpace(formatExpression))
                return ValidationResult.Success;

            CompositeFormat compositeFormat;
            try
            {
                compositeFormat = CompositeFormat.Parse(formatExpression);
            }
            catch (FormatException)
            {
                var invalidFormatMessage = FlowBloxResourceUtil.GetLocalizedString("FormatFlowBlock_Validation_InvalidFormatExpression");
                return new ValidationResult(invalidFormatMessage, [validationContext.MemberName]);
            }

            var placeholderCount = compositeFormat.MinimumArgumentCount;

            var linkedFieldCount = block.FormatParameterDefinitions?.Count ?? 0;

            if (placeholderCount != linkedFieldCount)
            {
                var errorMessage = string.Format(FlowBloxResourceUtil.GetLocalizedString(
                    "FormatFlowBlock_Validation_InvalidFieldElementCount"), placeholderCount, linkedFieldCount);
                return new ValidationResult(errorMessage, [validationContext.MemberName]);
            }

            return ValidationResult.Success;
        }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(FormatExpression));
            properties.Add(nameof(AllowEmptyFormatExpression));
            return properties;
        }

        public override bool Execute(Runtime.BaseRuntime runtime, object data)
        {
            return base.Invoke(runtime, data, () =>
            {
                string formatExpression;
                if (AllowEmptyFormatExpression)
                    formatExpression = this.Resolve(x => x.FormatExpression);
                else if (!this.ResolveOrCreateNotification(
                    x => x.FormatExpression,
                    out formatExpression,
                    runtime,
                    FormatFlowBlockNotifications.FormatExpressionMissingAfterResolving))
                    return;

                object[] parameters = FormatParameterDefinitions.Select(x => x.Field.Value).ToArray();
                var formattedValue = string.Format(formatExpression, parameters);
                GenerateResult(runtime, formattedValue);
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(FormatFlowBlockNotifications));
                return notificationTypes;
            }
        }

        public enum FormatFlowBlockNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Format expression is empty after field resolving")]
            FormatExpressionMissingAfterResolving
        }
    }
}
