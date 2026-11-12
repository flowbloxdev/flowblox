using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Extensions;

/// <summary>
/// Reads business configuration at consumption time without changing configured literals.
/// Use for strings and scalar properties in FlowBlocks, modifiers, providers and lifecycle callbacks.
/// </summary>
public static class FlowBloxFieldResolverExtensions
{
    /// <summary>
    /// Resolves inline string placeholders or a scalar field selection. Strings are never null.
    /// Empty values are allowed unless <paramref name="required"/> is true; zero and false are valid values.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Required resolution produced null, an empty string or whitespace.
    /// </exception>
    public static TValue Resolve<TTarget, TValue>(
        this TTarget target,
        Expression<Func<TTarget, TValue>> property,
        bool required = false)
        where TTarget : FlowBloxReactiveObject
    {
        var value = FlowBloxFieldHelper.GetSimplePropertyOrFieldValue(target, property);
        if (typeof(TValue) == typeof(string))
            value = (TValue)(object)(FlowBloxFieldHelper.ReplaceFieldsInString(value as string) ?? string.Empty);

        if (required && IsEmpty(value))
        {
            var member = ((MemberExpression)property.Body).Member;
            var display = member.GetCustomAttribute<DisplayAttribute>();
            var name = display == null ? member.Name : FlowBloxResourceUtil.GetDisplayName(display);
            throw new ValidationException(string.Format(
                FlowBloxResourceUtil.GetLocalizedString("FieldResolution_RequiredValueMissing", typeof(FlowBloxTexts)),
                name));
        }

        return value;
    }

    /// <summary>
    /// Resolves a property and returns false for null, empty strings or whitespace.
    /// Notifications, results and control flow remain the caller's responsibility.
    /// </summary>
    public static bool TryResolve<TTarget, TValue>(
        this TTarget target,
        Expression<Func<TTarget, TValue>> property,
        out TValue value)
        where TTarget : FlowBloxReactiveObject
    {
        value = target.Resolve(property);
        return !IsEmpty(value);
    }

    private static bool IsEmpty<TValue>(TValue value) =>
        value is null || value is string text && string.IsNullOrWhiteSpace(text);
}
