using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Util.Fields;

/// <summary>
/// Presents whether a field contains a value, without resolving references or exposing its content.
/// </summary>
public static class FieldValuePresentation
{
    public static string GetStateName(string value, bool required, bool pending = false)
    {
        if (pending)
            return "empty";

        if (!string.IsNullOrWhiteSpace(value))
            return "filled";

        return required ? "missing" : "empty";
    }

    public static string GetDescription(string value, bool required, bool pending = false)
    {
        var key = pending
            ? "FieldElement_PendingValue"
            : !string.IsNullOrWhiteSpace(value)
                ? "FieldElement_ValueSupplied"
                : required ? "FieldElement_RequiredInputEmpty" : "FieldElement_OptionalValueEmpty";

        return FlowBloxResourceUtil.GetLocalizedString(key, typeof(FlowBloxTexts));
    }

    public static SKImage GetIcon(string value, bool required, bool pending = false)
    {
        var color = GetStateName(value, required, pending) switch
        {
            "filled" => new SKColor(22, 163, 74),
            "missing" => new SKColor(220, 38, 38),
            _ => new SKColor(145, 153, 163)
        };

        return FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.circle, 16, color);
    }
}
