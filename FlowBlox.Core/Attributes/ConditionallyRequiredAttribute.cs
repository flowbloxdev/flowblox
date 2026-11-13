using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace FlowBlox.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ConditionallyRequiredAttribute : ValidationAttribute
    {
        public ConditionallyRequiredAttribute()
        {
            CheckActivationCondition = true;
        }

        /// <summary>
        /// If enabled, the <see cref="FlowBloxUIAttribute.ReadOnlyMethod"/> is evaluated:
        /// Required-check only applies if the property is currently editable.
        /// </summary>
        public bool CheckReadOnly { get; set; }

        /// <summary>
        /// If enabled, the <see cref="ActivationConditionAttribute"/> is evaluated:
        /// Required-check only applies if the property is currently activated based on the condition.
        /// </summary>
        public bool CheckActivationCondition { get; set; }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var target = validationContext.ObjectInstance;
            var property = validationContext.ObjectType.GetProperty(validationContext.MemberName);

            if (property == null)
                return ValidationResult.Success;

            if (CheckReadOnly && IsDynamicallyReadOnly(target, property))
                return ValidationResult.Success;

            if (CheckActivationCondition && !IsActivated(target, property))
                return ValidationResult.Success;

            if (!string.IsNullOrEmpty(value?.ToString()))
                return ValidationResult.Success;

            var memberName = TryGetDisplayName(validationContext) ?? validationContext.MemberName;

            return new ValidationResult(string.Format(
                    FlowBloxResourceUtil.GetLocalizedString(
                        "ConditionallyRequiredAttribute_ValidationFailed_Message", typeof(FlowBloxTexts)), memberName), [validationContext.MemberName]);
        }

        private static string? TryGetDisplayName(ValidationContext validationContext)
        {
            try
            {
                return validationContext.DisplayName;
            }
            catch (InvalidOperationException)
            {
                // Happens when DisplayAttribute.ResourceType is set
                // but the resource key does not exist.
                return null;
            }
        }

        private static bool IsDynamicallyReadOnly(object target, PropertyInfo property)
        {
            var uiAttr = property.GetFlowBloxUIAttribute();

            return FlowBlockUIAttributeHelper.IsDynamicallyReadOnly(target, uiAttr);
        }

        private static bool IsActivated(object target, PropertyInfo property)
        {
            var activationAttr = property.GetCustomAttributes(typeof(ActivationConditionAttribute), true)
                                         .Cast<ActivationConditionAttribute>()
                                         .FirstOrDefault();

            if (activationAttr == null)
                return true;

            return activationAttr.IsActive(target);
        }
    }
}