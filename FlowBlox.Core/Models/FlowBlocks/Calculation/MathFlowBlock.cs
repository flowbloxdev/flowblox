using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.FlowBlocks.Calculation
{
    [Display(Name = "MathFlowBlock_DisplayName", Description = "MathFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class MathFlowBlock : BaseSingleResultFlowBlock
    {
        public override FieldTypes DefaultResultFieldType => FieldTypes.Integer;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.Calculation;

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calculator_variant, 16, SKColors.DarkGoldenrod);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.calculator_variant, 32, SKColors.DarkGoldenrod);

        [Required]
        [Display(Name = "MathFlowBlock_Expression", Description = "MathFlowBlock_Expression_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxTextBox(IsCodingMode = true, MultiLine = true)]
        public string MathExpression { get; set; }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(MathExpression));
            return properties;
        }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (string.IsNullOrWhiteSpace(MathExpression))
                {
                    runtime.Report("Math expression is empty.", FlowBloxLogLevel.Warning);
                    CreateNotification(runtime, MathFlowBlockNotifications.ExpressionEmpty);
                    GenerateResult(runtime);
                    return;
                }

                string expressionText;
                Dictionary<string, object> parameters;
                try
                {
                    expressionText = FlowBloxMathExpressionHelper.ReplaceFieldsInMathExpression(MathExpression, out parameters);
                }
                catch (Exception ex)
                {
                    runtime.Report($"Failed to prepare math expression.", FlowBloxLogLevel.Error, e: ex);
                    CreateNotification(runtime, MathFlowBlockNotifications.UnsupportedFieldType);
                    GenerateResult(runtime);
                    return;
                }

                var expr = new NCalc.Expression(expressionText, NCalc.EvaluateOptions.IgnoreCase);

                foreach (var p in parameters)
                    expr.Parameters[p.Key] = p.Value;

                object result;
                try
                {
                    result = expr.Evaluate();
                }
                catch (Exception ex)
                {
                    runtime.Report($"Failed to evaluate math expression '{MathExpression}'. Details: {ex.Message}", FlowBloxLogLevel.Error, e: ex);
                    CreateNotification(runtime, MathFlowBlockNotifications.EvaluationFailed);
                    GenerateResult(runtime);
                    return;
                }


                string resultString = FieldResultFormatter.FormatResult(ResultField, result);

                if (string.IsNullOrWhiteSpace(resultString))
                    CreateNotification(runtime, MathFlowBlockNotifications.InvalidResult);

                GenerateResult(runtime, resultString);
            });
        }

        public override List<Type> NotificationTypes
        {
            get
            {
                var notificationTypes = base.NotificationTypes;
                notificationTypes.Add(typeof(MathFlowBlockNotifications));
                return notificationTypes;
            }
        }

        public enum MathFlowBlockNotifications
        {
            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Math expression is empty")]
            ExpressionEmpty,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "Math expression evaluation failed")]
            EvaluationFailed,

            [FlowBloxNotification(NotificationType = NotificationType.Error)]
            [Display(Name = "Unsupported field type used in math expression")]
            UnsupportedFieldType,

            [FlowBloxNotification(NotificationType = NotificationType.Warning)]
            [Display(Name = "Math expression produced an invalid result")]
            InvalidResult
        }
    }
}
