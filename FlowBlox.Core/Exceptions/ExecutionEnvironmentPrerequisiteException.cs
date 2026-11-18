using FlowBlox.Core.Util.Execution;

namespace FlowBlox.Core.Exceptions
{
    /// <summary>
    /// A dependency of the execution environment is missing or cannot be initialized.
    /// Missing FlowBlox configuration values or associations are not environment prerequisites.
    /// QuickStartTargetType connects the failure to the extension's existing popup provider.
    /// </summary>
    public sealed class ExecutionEnvironmentPrerequisiteException : InvalidOperationException
    {
        /// <summary>
        /// Describes a missing installation or initialization failure with actionable Quick Start guidance.
        /// </summary>
        /// <param name="componentName">Short English component name included in the detailed setup guidance.</param>
        /// <param name="quickStartTargetType">
        /// The target type of the component's registered popup provider; extension types are supported.
        /// Its display name identifies the Help menu entry. This does not register a provider automatically.
        /// </param>
        /// <param name="location">The resolved location of the executable, library or model that failed.</param>
        /// <param name="innerException">The initialization cause, or null when a required component is missing.</param>
        public ExecutionEnvironmentPrerequisiteException(
            string componentName,
            Type quickStartTargetType,
            string location,
            Exception? innerException = null)
            : base(ExecutionEnvironmentPrerequisites.CreateMessage(componentName, quickStartTargetType, location, innerException), innerException)
        {
            ComponentName = componentName;
            QuickStartTargetType = quickStartTargetType;
            Location = location;
        }

        /// <summary>
        /// Gets the short English name of the unavailable execution component.
        /// </summary>
        public string ComponentName { get; }

        /// <summary>
        /// Gets the popup-provider target type whose Quick Start explains setup, including extension targets.
        /// </summary>
        public Type QuickStartTargetType { get; }

        /// <summary>
        /// Gets the resolved environment dependency location included in the detailed error.
        /// </summary>
        public string Location { get; }
    }
}
