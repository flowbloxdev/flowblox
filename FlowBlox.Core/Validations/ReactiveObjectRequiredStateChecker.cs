using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.FlowBlocks;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace FlowBlox.Core.Validations
{
    public static class ReactiveObjectRequiredStateChecker
    {
        public static bool IsRequired(object target, PropertyInfo property)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (property == null)
                throw new ArgumentNullException(nameof(property));

            if (property.GetCustomAttribute<RequiredAttribute>(true) != null)
                return true;

            var conditionallyRequired = property.GetCustomAttribute<ConditionallyRequiredAttribute>(true);
            if (conditionallyRequired != null && IsConditionallyRequired(target, property, conditionallyRequired))
                return true;

            var requiredIf = property.GetCustomAttribute<RequiredIfAttribute>(true);
            if (requiredIf != null && EvaluateRequiredIf(target, requiredIf))
                return true;

            var activation = property.GetCustomAttribute<ActivationConditionAttribute>(true);
            return activation?.IsRequired == true && activation.IsActive(target);
        }

        public static bool IsConditionallyRequired(
            object target,
            PropertyInfo property,
            ConditionallyRequiredAttribute attribute)
        {
            if (attribute.CheckReadOnly)
            {
                var uiAttribute = property.GetFlowBloxUIAttribute();
                if (FlowBlockUIAttributeHelper.IsDynamicallyReadOnly(target, uiAttribute))
                    return false;
            }

            if (!attribute.CheckActivationCondition)
                return true;

            var activation = property.GetCustomAttribute<ActivationConditionAttribute>(true);
            return activation == null || activation.IsActive(target);
        }

        private static bool EvaluateRequiredIf(object target, RequiredIfAttribute attribute)
        {
            var conditionProperty = target.GetType().GetProperty(
                attribute.MemberName,
                BindingFlags.Public | BindingFlags.Instance);

            return conditionProperty == null || Equals(conditionProperty.GetValue(target), attribute.Value);
        }
    }
}
