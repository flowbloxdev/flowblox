using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using Texts = FlowBlox.UICore.Resources.AboutWindow;

namespace FlowBlox.UICore.ViewModels;

public sealed class AboutViewModel
{
    public AboutViewModel(Assembly assembly)
    {
        ProductName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "FlowBlox";
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString() ?? string.Empty;
        Version = string.Format(Texts.Version, FlowBloxVersionHelper.GetDisplayVersion(version));
        Description = FlowBloxResourceUtil.GetLocalizedString("Tagline", typeof(Resources.SplashWindow));
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;
        copyright = string.IsNullOrWhiteSpace(copyright)
            ? string.Format(Texts.DefaultCopyright, DateTime.Now.Year) : copyright;
        Copyright = Regex.Replace(Regex.Replace(copyright, @"(©\s*\d{4})\s+", "$1" + Environment.NewLine),
            @"\.\s+", "." + Environment.NewLine);
    }

    public string ProductName { get; }
    public string Version { get; }
    public string RuntimeVersion { get; } = RuntimeInformation.FrameworkDescription;
    public string Description { get; }
    public string Copyright { get; }
}
