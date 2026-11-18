using System.ComponentModel.DataAnnotations;
using System.Net;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Models.WebServer;

[Display(Name = "WebServerConfiguration_DisplayName", Description = "WebServerConfiguration_Description", ResourceType = typeof(FlowBloxTexts))]
public sealed class WebServerConfiguration : FlowBloxReactiveObject
{
    [Required]
    [CustomValidation(typeof(WebServerConfiguration), nameof(ValidateBindAddress))]
    [Display(Name = "WebServerConfiguration_BindAddress", Description = "WebServerConfiguration_BindAddress_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    [FlowBloxUI(UISelectionButtons.None)]
    public string BindAddress { get; set; } = "127.0.0.1";

    [Range(1024, 65535)]
    [Display(Name = "WebServerConfiguration_Port", Description = "WebServerConfiguration_Port_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    [FlowBloxUI(UISelectionButtons.None)]
    public int Port { get; set; } = 9001;

    [Display(Name = "WebServerConfiguration_CertificateFile", Description = "WebServerConfiguration_CertificateFile_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
    [FlowBloxUI(UISelectionButtons.FL)]
    public string CertificateFile { get; set; } = string.Empty;

    [Display(Name = "WebServerConfiguration_CertificatePassword", Description = "WebServerConfiguration_CertificatePassword_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
    [FlowBloxTextBox(IsPassword = true)]
    [FlowBloxUI(UISelectionButtons.None)]
    public string CertificatePassword { get; set; } = string.Empty;

    [Display(Name = "WebServerConfiguration_AppPassword", Description = "WebServerConfiguration_AppPassword_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    [FlowBloxTextBox(IsPassword = true)]
    [FlowBloxUI(UISelectionButtons.None)]
    public string AppPassword { get; set; } = string.Empty;

    public static ValidationResult ValidateBindAddress(string value, ValidationContext context) =>
        IPAddress.TryParse(value, out _) ? ValidationResult.Success :
        new ValidationResult(FlowBloxResourceUtil.GetLocalizedString("WebServerConfiguration_InvalidAddress"), [context.MemberName]);

}
