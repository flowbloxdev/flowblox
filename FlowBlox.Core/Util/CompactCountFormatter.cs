using System.Globalization;

namespace FlowBlox.Core.Util;

/// <summary>
/// Displays exact small counts and compact large counts without rounding into the next unit.
/// </summary>
public static class CompactCountFormatter
{
    public static string Format(long count, CultureInfo culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        count = Math.Max(0, count);
        if (count < 100_000)
            return count.ToString("N0", culture);

        var (divisor, suffix) = count switch
        {
            >= 1_000_000_000_000 => (1_000_000_000_000m, "T"),
            >= 1_000_000_000 => (1_000_000_000m, "B"),
            >= 1_000_000 => (1_000_000m, "M"),
            _ => (1_000m, "k")
        };
        var scaled = count / divisor;
        var compact = scaled >= 100 ? decimal.Truncate(scaled) : decimal.Truncate(scaled * 10) / 10;
        return compact.ToString("0.#", culture) + suffix;
    }
}
