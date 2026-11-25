namespace FlowBlox.Core.Util.Fields;

/// <summary>
/// Shared separator and paste semantics for project input list editors.
/// </summary>
public static class FieldListPresentation
{
    public static string ResolveSeparator(string separator) => string.IsNullOrEmpty(separator)
        ? "\n"
        : separator.Replace(@"\r", "\r").Replace(@"\n", "\n").Replace(@"\t", "\t");

    public static string[] Split(string value, string separator)
    {
        var resolved = ResolveSeparator(separator);
        if (resolved == "\n" || resolved == "\r\n" || resolved == "\r")
        {
            value = (value ?? string.Empty).ReplaceLineEndings("\n");
            resolved = "\n";
        }

        return (value ?? string.Empty).Split(resolved, StringSplitOptions.None);
    }

    /// <summary>
    /// Overwrites successive existing rows from the current row, adding only missing rows.
    /// </summary>
    public static void Apply(IList<string> entries, int index, string value, string separator)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (index < 0 || index >= entries.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        foreach (var part in Split(value, separator))
        {
            if (index < entries.Count)
                entries[index] = part;
            else
                entries.Add(part);

            index++;
        }
    }
}
