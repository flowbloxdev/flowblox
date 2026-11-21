namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi;

/// <summary>
/// Uses the API's relevance score, with download totals as a fallback for older servers.
/// </summary>
public static class ProjectSpaceRelevance
{
    public static double GetScore(long downloadCount, double? relevance = null)
    {
        return relevance.HasValue && double.IsFinite(relevance.Value)
            ? relevance.Value
            : Math.Max(0, downloadCount);
    }
}
