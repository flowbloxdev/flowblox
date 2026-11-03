using System;

namespace FlowBlox.AvaloniaDesktop.Services.Updates
{
    public sealed class InstallerUpdateInfo
    {
        public InstallerUpdateInfo(Version version, string installerUrl)
        {
            Version = version;
            InstallerUrl = installerUrl;
        }

        public Version Version { get; }

        public string InstallerUrl { get; }
    }
}
