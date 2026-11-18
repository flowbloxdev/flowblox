using System.Text.RegularExpressions;

namespace FlowBlox.Core.Util;

public static class WebLinks
{
    private static readonly Regex Pattern = new(
        "https?://[^\\s<>\"\u201c\u201d]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    public static IEnumerable<(int Start, int Length, Uri Uri)> Find(string text)
    {
        foreach (Match match in Pattern.Matches(text ?? string.Empty))
        {
            var value = match.Value.TrimEnd('.', ',', ';', ':');
            while (value.Length > 0 && HasUnmatchedClosingBracket(value))
                value = value[..^1].TrimEnd('.', ',', ';', ':');

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                yield return (match.Index, value.Length, uri);
        }
    }

    private static bool HasUnmatchedClosingBracket(string value)
    {
        var closing = value[^1];
        var opening = closing switch { ')' => '(', ']' => '[', '}' => '{', _ => '\0' };
        return opening != '\0' && value.Count(character => character == closing) > value.Count(character => character == opening);
    }
}
