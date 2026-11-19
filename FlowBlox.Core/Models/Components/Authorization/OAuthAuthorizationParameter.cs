using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Models.Components.Authorization;

public sealed class OAuthAuthorizationParameter : FlowBloxReactiveObject
{
    [Required]
    [FlowBloxUI(UISelectionButtons.None)]
    [Display(Name = "OAuthAuthorizationParameter_Name", ResourceType = typeof(FlowBloxTexts))]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "OAuthAuthorizationParameter_Value", ResourceType = typeof(FlowBloxTexts))]
    public string Value { get; set; } = string.Empty;

    public override string ToString() => Name;
}
