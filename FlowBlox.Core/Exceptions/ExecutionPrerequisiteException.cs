using FlowBlox.Core.Util.Execution;

namespace FlowBlox.Core.Exceptions
{
    /// <summary>
    /// A local execution component is missing or cannot be initialized.
    /// QuickStartTargetType connects the failure to the extension's existing popup provider.
    /// </summary>
    public sealed class ExecutionPrerequisiteException : InvalidOperationException
    {
        /// <summary>
        /// Describes a missing installation or initialization failure with actionable Quick Start guidance.
        /// </summary>
        /// <param name="componentName">Short English component name used in the node notification.</param>
        /// <param name="quickStartTargetType">
        /// The target type of the component's registered popup provider; extension types are supported.
        /// Its display name identifies the Help menu entry. This does not register a provider automatically.
        /// </param>
        /// <param name="location">The resolved path or configuration location that failed.</param>
        /// <param name="innerException">The initialization cause, or null when a required component is missing.</param>
        public ExecutionPrerequisiteException(
            string componentName,
            Type quickStartTargetType,
            string location,
            Exception? innerException = null)
            : base(ExecutionPrerequisites.CreateMessage(componentName, quickStartTargetType, location, innerException), innerException)
        {
            ComponentName = componentName;
            QuickStartTargetType = quickStartTargetType;
            Location = location;
        }

        /// <summary>
        /// Compact English text for the notification below the FlowBlock node.
        /// </summary>
        public string NotificationMessage => ComponentName + " unavailable";

        /// <summary>
        /// Gets the short English name of the unavailable execution component.
        /// </summary>
        public string ComponentName { get; }
        /// <summary>
        /// Gets the popup-provider target type whose Quick Start explains setup, including extension targets.
        /// </summary>
        public Type QuickStartTargetType { get; }
        /// <summary>
        /// Gets the resolved executable/model path or configuration location included in the detailed error.
        /// </summary>
        public string Location { get; }
    }
}
