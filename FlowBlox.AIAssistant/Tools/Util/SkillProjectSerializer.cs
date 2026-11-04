using FlowBlox.Core.Models.Project;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static class SkillProjectSerializer
    {
        public static FlowBloxProject FromFile(string projectFile)
        {
            using var extensionLoading = new DisableExtensionLoading();
            return FlowBloxProject.FromFile(projectFile);
        }

        public static FlowBloxProject FromJson(string projectJson)
        {
            using var extensionLoading = new DisableExtensionLoading();
            return FlowBloxProject.FromJsonContents(projectJson, extensionsJson: null);
        }
    }
}
