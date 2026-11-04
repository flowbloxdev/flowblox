using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DistributedData;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static class SkillProjectApprovalStore
    {
        public const string OptionName = "AI.ApprovedSkillProjects";
        private static readonly object Sync = new();

        public static string GetProjectKey(string projectFile)
        {
            var fullPath = Path.GetFullPath(projectFile);
            var fileName = Path.GetFileName(fullPath);
            var templatePath = DistributedDataCatalog.ResolvePath(DistributedDataCatalog.SkillProjectType, fileName);
            if (templatePath != null && string.Equals(Path.GetFullPath(templatePath), fullPath, StringComparison.OrdinalIgnoreCase))
                return fileName;

            return fullPath;
        }

        public static bool IsApproved(string projectFile)
        {
            if (string.IsNullOrWhiteSpace(projectFile))
                return false;

            var key = GetProjectKey(projectFile);
            return ReadEntries().Contains(key, StringComparer.OrdinalIgnoreCase);
        }

        public static void Approve(string projectFile)
        {
            var key = GetProjectKey(projectFile);
            lock (Sync)
            {
                var options = FlowBloxOptions.GetOptionInstance();
                var option = options.GetOption(OptionName);
                var entries = ReadEntries();
                if (entries.Contains(key, StringComparer.OrdinalIgnoreCase))
                    return;

                var previousValue = option.PersistentValue;
                option.PersistentValue = string.Join(Environment.NewLine, entries.Append(key));
                try
                {
                    options.Save();
                }
                catch
                {
                    option.PersistentValue = previousValue;
                    throw;
                }
            }
        }

        private static List<string> ReadEntries() =>
            (FlowBloxOptions.GetOptionInstance().GetOption(OptionName).PersistentValue ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Trim())
                .Where(entry => entry.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
