using System.Reflection;

namespace FlowBlox.Core.Constants
{
    public class GlobalPaths
    {
        public static string CurrentPath => Assembly.GetExecutingAssembly().GetModules()[0].FullyQualifiedName;
        public static string CurrentDirectory => Path.GetDirectoryName(CurrentPath);
        public static string UserDataDirectory { get; private set; } = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "FlowBlox");
        public static string GlobalConfigurationXml => Path.Combine(UserDataDirectory, "global_configuration.xml");
        public static string RecentProjectsPath => Path.Combine(UserDataDirectory, "recentProjects.json");

        /// <summary>
        /// Selects the desktop user's data directory before a service initializes options or launcher stores.
        /// </summary>
        public static void UseUserDataDirectory(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            UserDataDirectory = Path.GetFullPath(directory);
        }
    }
}
