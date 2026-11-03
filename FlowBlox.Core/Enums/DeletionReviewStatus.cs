namespace FlowBlox.Enums;

/// <summary>
/// Describes the follow-up needed for an affected dependency after deletion.
/// </summary>
public enum DeletionReviewStatus
{
    /// <summary>
    /// The dependency is handled automatically and needs no manual follow-up.
    /// </summary>
    Information,
    /// <summary>
    /// Reviewing the affected configuration is recommended.
    /// </summary>
    ReviewRecommended,
    /// <summary>
    /// The affected configuration requires manual correction.
    /// </summary>
    ManualActionRequired
}
