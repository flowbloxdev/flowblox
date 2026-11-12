using System.Text.RegularExpressions;

namespace FlowBlox.AIAssistant.Tools;

/// <summary>
/// Resolves provider-qualified tool names without guessing between ambiguous registrations.
/// </summary>
internal sealed class ToolNameResolver
{
    private static readonly Regex QualifierSeparator = new(
        @"[-_./:]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HashSet<string> _exactNames;
    private readonly Dictionary<string, string[]> _namesIgnoringCase;
    private readonly Func<string, IEnumerable<string>>[] _strategies;

    public ToolNameResolver(IEnumerable<string> names)
    {
        _exactNames = new HashSet<string>(names, StringComparer.Ordinal);
        _namesIgnoringCase = _exactNames
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        _strategies = [MatchExact, MatchIgnoringCase, MatchWithoutPrefix, MatchLastPart];
    }

    public string? Resolve(string name)
    {
        foreach (var strategy in _strategies)
        {
            var matches = strategy(name).Distinct(StringComparer.Ordinal).ToArray();
            if (matches.Length == 1)
                return matches[0];

            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Ambiguous tool '{name}'; please pass the exact name. Candidates: " +
                    string.Join(", ", matches.OrderBy(candidate => candidate, StringComparer.Ordinal)) + ".");
            }
        }

        return null;
    }

    private IEnumerable<string> MatchExact(string name)
    {
        return _exactNames.Contains(name) ? [name] : [];
    }

    private IEnumerable<string> MatchIgnoringCase(string name)
    {
        return _namesIgnoringCase.TryGetValue(name, out var matches) 
            ? matches 
            : [];
    }

    private IEnumerable<string> MatchWithoutPrefix(string name)
    {
        foreach (Match separator in QualifierSeparator.Matches(name))
        {
            var suffix = name[(separator.Index + separator.Length)..];
            var exact = MatchExact(suffix).ToArray();
            if (exact.Length > 0)
                return exact;

            var matches = MatchIgnoringCase(suffix).ToArray();
            if (matches.Length > 0)
                return matches;
        }

        return [];
    }

    private IEnumerable<string> MatchLastPart(string name)
    {
        var lastPart = QualifierSeparator.Split(name).Last();
        if (string.IsNullOrEmpty(lastPart))
            return [];

        return _exactNames.Where(candidate => candidate.EndsWith(lastPart, StringComparison.OrdinalIgnoreCase));
    }
}
