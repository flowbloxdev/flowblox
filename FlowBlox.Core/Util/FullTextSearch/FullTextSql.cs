using System.Text.RegularExpressions;

namespace FlowBlox.Core.Util.FullTextSearch;

/// <summary>
/// Recognizes the portable search functions and a single physical source table.
/// Complex SQL stays available through the ordinary SQL execution path.
/// </summary>
internal sealed class FullTextSql
{
    private const string Identifier = "(?:\\[[^\\]]+\\]|\"(?:[^\"]|\"\")+\"|`(?:[^`]|``)+`|[\\p{L}_][\\p{L}\\p{N}_$]*)";
    private static readonly Regex MatchFunction = new("\\bFULLTEXT_MATCH\\s*\\(\\s*(?<columns>(?:" + Identifier + "\\s*,\\s*)*)(?<search>[@:][\\w]+|'(?:[^']|'')*')\\s*\\)", RegexOptions.IgnoreCase);
    private static readonly Regex RankFunction = new(@"\bFULLTEXT_RANK\s*\(\s*\)", RegexOptions.IgnoreCase);
    private static readonly Regex Words = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly HashSet<string> Clauses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "ORDER", "GROUP", "HAVING", "LIMIT", "OFFSET", "FETCH", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "ON", "SET", "VALUES", "RETURNING"
    };

    public string Statement { get; }
    public string[] TableParts { get; }
    public bool[] QuotedTableParts { get; }
    public string Alias { get; }
    public bool QuotedAlias { get; }
    public string? SearchArgument { get; }
    public string[] SearchColumns { get; }
    public bool[] QuotedSearchColumns { get; }
    public bool IsCreate { get; }
    public bool IsSelect { get; }

    public FullTextSql(string statement)
    {
        Statement = statement.Trim().TrimEnd(';').Trim();
        var masked = Mask(Statement);
        if (masked.Contains(';') || Regex.IsMatch(masked, @"\b(JOIN|UNION|INTERSECT|EXCEPT|GROUP|HAVING|DISTINCT)\b", RegexOptions.IgnoreCase))
            throw new NotSupportedException("Native full-text search requires one physical table and a single, non-aggregated SQL statement.");

        IsSelect = Regex.IsMatch(masked, @"^\s*SELECT\b", RegexOptions.IgnoreCase);
        IsCreate = Regex.IsMatch(masked, @"^\s*CREATE\s+TABLE\b", RegexOptions.IgnoreCase);
        var prefix = IsSelect ? @"\bFROM\s+"
            : IsCreate ? @"^\s*CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?"
            : Regex.IsMatch(masked, @"^\s*INSERT\b", RegexOptions.IgnoreCase) ? @"^\s*INSERT\s+(?:OR\s+\w+\s+)?INTO\s+"
            : Regex.IsMatch(masked, @"^\s*UPDATE\b", RegexOptions.IgnoreCase) ? @"^\s*UPDATE\s+"
            : Regex.IsMatch(masked, @"^\s*DELETE\s+FROM\b", RegexOptions.IgnoreCase) ? @"^\s*DELETE\s+FROM\s+"
            : throw new NotSupportedException("Full-text indexing supports CREATE TABLE, INSERT, UPDATE, DELETE and single-table SELECT statements.");
        var source = Regex.Match(masked, prefix, RegexOptions.IgnoreCase);
        var table = source.Success
            ? Regex.Match(Statement[(source.Index + source.Length)..], "^(?<table>" + Identifier + "(?:\\s*\\.\\s*" + Identifier + "){0,1})", RegexOptions.IgnoreCase)
            : Match.Empty;
        if (!table.Success || (IsSelect && Regex.Matches(masked, @"\bFROM\b", RegexOptions.IgnoreCase).Count != 1))
            throw new NotSupportedException("Cannot determine a single physical table for full-text indexing.");
        TableParts = Regex.Matches(table.Groups["table"].Value, Identifier).Select(match => Unquote(match.Value)).ToArray();
        QuotedTableParts = Regex.Matches(table.Groups["table"].Value, Identifier).Select(match => match.Value[0] is '[' or '"' or '`').ToArray();
        Alias = TableParts[^1];
        QuotedAlias = QuotedTableParts[^1];
        if (IsSelect)
        {
            var tail = Statement[(source.Index + source.Length + table.Length)..];
            if (Regex.IsMatch(tail, @"^\s*[.(]"))
                throw new NotSupportedException("Use a physical table in the current database, optionally qualified by its schema.");
            var alias = Regex.Match(tail, "^\\s+(?:AS\\s+)?(?<alias>" + Identifier + ")", RegexOptions.IgnoreCase);
            if (alias.Success && (alias.Groups["alias"].Value[0] is '[' or '"' or '`'
                || !Clauses.Contains(Unquote(alias.Groups["alias"].Value))))
            {
                Alias = Unquote(alias.Groups["alias"].Value);
                QuotedAlias = alias.Groups["alias"].Value[0] is '[' or '"' or '`';
            }
            if (Regex.IsMatch(tail, "^\\s*(?:AS\\s+)?" + Identifier + "\\s*,|^\\s*,", RegexOptions.IgnoreCase))
                throw new NotSupportedException("Full-text search does not support multiple source tables.");
        }
        var matches = MatchFunction.Matches(Statement).Where(match => masked[match.Index] is not (' ' or '\u0001')).ToList();
        if (matches.Count > 1)
            throw new NotSupportedException("Use one FULLTEXT_MATCH(search) expression per SQL statement.");
        var search = matches.SingleOrDefault();
        SearchArgument = search?.Groups["search"].Value;
        var columns = Regex.Matches(search?.Groups["columns"].Value ?? string.Empty, Identifier);
        SearchColumns = columns.Select(match => Unquote(match.Value)).ToArray();
        QuotedSearchColumns = columns.Select(match => match.Value[0] is '[' or '"' or '`').ToArray();
        if (Regex.IsMatch(masked, @"\bFULLTEXT_MATCH\b", RegexOptions.IgnoreCase) && SearchArgument == null)
            throw new NotSupportedException("FULLTEXT_MATCH requires optional text column names followed by one bound SQL parameter or literal string.");
        if (SearchArgument == null && Regex.IsMatch(masked, @"\bFULLTEXT_RANK\b", RegexOptions.IgnoreCase))
            throw new NotSupportedException("FULLTEXT_RANK() requires FULLTEXT_MATCH(search) in the same query.");
    }

    public string Rewrite(string predicate, string rank)
    {
        var masked = Mask(Statement);
        var replacements = MatchFunction.Matches(Statement).Cast<Match>()
            .Select(match => (Match: match, Value: predicate))
            .Concat(RankFunction.Matches(Statement).Cast<Match>().Select(match => (Match: match, Value: rank)))
            .Where(item => masked[item.Match.Index] is not (' ' or '\u0001'))
            .OrderByDescending(item => item.Match.Index);
        var result = Statement;
        foreach (var item in replacements)
            result = result.Remove(item.Match.Index, item.Match.Length).Insert(item.Match.Index, item.Value);
        var order = Regex.Match(masked, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase);
        if (order.Success)
        {
            // Original offsets still apply only before the first portable expression.
            var outputOrder = Regex.Match(Mask(result), @"\bORDER\s+BY\b", RegexOptions.IgnoreCase);
            return result.Insert(outputOrder.Index + outputOrder.Length, " " + rank + " DESC,");
        }
        var outputTail = Regex.Match(Mask(result), @"\b(LIMIT|OFFSET|FETCH)\b", RegexOptions.IgnoreCase);
        return result.Insert(outputTail.Success ? outputTail.Index : result.Length, " ORDER BY " + rank + " DESC ");
    }

    public static string[] Terms(string value) => Words.Matches(value)
        .Select(match => match.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(64)
        .ToArray();

    private static string Unquote(string identifier) => identifier[0] switch
    {
        '[' => identifier[1..^1],
        '"' => identifier[1..^1].Replace("\"\"", "\""),
        '`' => identifier[1..^1].Replace("``", "`"),
        _ => identifier
    };

    internal static string Mask(string statement) => Regex.Replace(statement,
        "'(?:[^']|'')*'|--[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|\"(?:[^\"]|\"\")*\"|`(?:[^`]|``)*`|\\[(?:[^\\]]|\\]\\])*\\]",
        match => new string(match.Value[0] is '"' or '`' or '[' ? '\u0001' : ' ', match.Length));
}
