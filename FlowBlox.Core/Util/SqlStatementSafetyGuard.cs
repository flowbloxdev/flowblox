using FlowBlox.Core.Constants;
namespace FlowBlox.Core.Util
{
    internal static class SqlStatementSafetyGuard
    {

        private static readonly HashSet<string> DestructiveKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "DELETE",
            "DROP",
            "TRUNCATE"
        };

        public static void EnsureAllowed(string sqlStatement)
        {
            var option = FlowBloxOptions.GetOptionInstance()
                .GetOption(OptionsConstants.SQLAllowDestructiveStatements);
            if (option != null && bool.TryParse(option.Value, out var allowed) && allowed)
                return;

            var keyword = SqlUnquotedTokensReader.Read(sqlStatement).FirstOrDefault(DestructiveKeywords.Contains);
            if (keyword == null)
                return;

            throw new NotSupportedException(
                $"Destructive SQL statement '{keyword.ToUpperInvariant()}' is disabled by application option " +
                $"'{OptionsConstants.SQLAllowDestructiveStatements}'. Enable this option manually in FlowBlox only when the operation is intended.");
        }
    }
}
