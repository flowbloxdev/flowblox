using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Models.Components.Modifier;

/// <summary>
/// Exposes a text's character count for ordinary numeric calculations and flow prerequisites.
/// </summary>
[Display(Name = "TextLengthModifier_DisplayName", Description = "TextLengthModifier_Description", ResourceType = typeof(FlowBloxTexts))]
public sealed class TextLengthModifier : ModifierBase
{
    public override string Modify(BaseRuntime runtime, string value) =>
        (value?.Length ?? 0).ToString(CultureInfo.InvariantCulture);

    public override string ToString() =>
        FlowBloxResourceUtil.GetLocalizedString("TextLengthModifier_DisplayName", typeof(FlowBloxTexts));
}
