using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Core.Util.Execution
{
    /// <summary>
    /// Locates required local programs without launching them or downloading components.
    /// Callers choose the existing lifecycle stage and pass their resolved configuration.
    /// </summary>
    public static class ExecutionPrerequisites
    {
        /// <summary>
        /// Locates an installed program or throws an execution failure pointing to its setup guide.
        /// </summary>
        /// <typeparam name="TQuickStartTarget">The target type of the registered Quick Start popup provider.</typeparam>
        /// <param name="componentName">Short English program name for the node notification.</param>
        /// <param name="configuredPath">The field-resolved program path or executable name.</param>
        /// <param name="additionalDirectory">An optional directory searched before the application directory and PATH.</param>
        /// <returns>The absolute path of the existing executable.</returns>
        /// <remarks>This checks file presence only; it neither launches nor downloads the program.</remarks>
        /// <exception cref="ExecutionPrerequisiteException">The program could not be located.</exception>
        public static string RequireExecutable<TQuickStartTarget>(
            string componentName,
            string configuredPath,
            string? additionalDirectory = null)
        {
            var executable = FindExecutable(configuredPath, additionalDirectory);
            if (executable == null)
                throw new ExecutionPrerequisiteException(componentName, typeof(TQuickStartTarget), configuredPath);

            return executable;
        }

        /// <summary>
        /// Finds an existing executable by explicit path, then by additional directory, application directory and PATH.
        /// </summary>
        /// <param name="configuredPath">The resolved path or program name; an invalid explicit path has no fallback.</param>
        /// <param name="additionalDirectory">An optional search directory for a bare executable name.</param>
        /// <returns>An absolute path, or null when no matching file exists.</returns>
        /// <remarks>Windows also accepts an extensionless name when the matching .exe file exists.</remarks>
        public static string? FindExecutable(string configuredPath, string? additionalDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                return null;

            var executableNames = OperatingSystem.IsWindows() && !Path.HasExtension(configuredPath)
                ? new[] { configuredPath, configuredPath + ".exe" }
                : new[] { configuredPath };
            foreach (var executableName in executableNames)
            {
                if (File.Exists(executableName))
                    return Path.GetFullPath(executableName);
            }

            if (Path.IsPathRooted(configuredPath)
                || configuredPath.Contains(Path.DirectorySeparatorChar)
                || configuredPath.Contains(Path.AltDirectorySeparatorChar))
                return null;

            var directories = new[] { additionalDirectory, AppContext.BaseDirectory }
                .Concat((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator));
            foreach (var directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                foreach (var executableName in executableNames)
                {
                    var candidate = Path.Combine(directory.Trim('"'), executableName);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
            }

            return null;
        }

        /// <summary>
        /// Uses the same target display name as the generic popup provider's Help menu entry.
        /// Extension target types should declare a localized DisplayAttribute.
        /// </summary>
        /// <param name="targetType">The popup provider's target type.</param>
        /// <param name="fallback">The title used when the target has no display attribute.</param>
        /// <returns>The localized display name, the supplied fallback, or the type name.</returns>
        public static string GetQuickStartName(Type targetType, string? fallback = null)
        {
            var display = targetType.GetCustomAttribute<DisplayAttribute>();
            return display == null ? fallback ?? targetType.Name : FlowBloxResourceUtil.GetDisplayName(display);
        }

        /// <summary>
        /// Builds detailed localized setup guidance for logs, error details and the Runner result.
        /// </summary>
        /// <param name="componentName">The unavailable program or runtime.</param>
        /// <param name="quickStartTargetType">The target type identifying the matching Help Quick Start.</param>
        /// <param name="location">The resolved path or configuration location that failed.</param>
        /// <param name="exception">The initialization cause, or null for a missing installation.</param>
        /// <returns>The localized error containing the setup menu path and any underlying cause.</returns>
        public static string CreateMessage(
            string componentName,
            Type quickStartTargetType,
            string location,
            Exception? exception)
        {
            var key = exception == null
                ? "ExecutionPrerequisite_Missing"
                : "ExecutionPrerequisite_InitializationFailed";
            return string.Format(
                CultureInfo.CurrentCulture,
                FlowBloxResourceUtil.GetLocalizedString(key, typeof(FlowBloxTexts)),
                componentName,
                location,
                GetQuickStartName(quickStartTargetType),
                exception?.GetBaseException().Message ?? string.Empty);
        }
    }
}
