using System.Data.Common;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Util.FullTextSearch;

internal sealed class MySqlFullTextSearch : FullTextSearchBase
{
    protected override DbTypes Provider => DbTypes.MySQL;
    protected override string Prerequisites => "MySQL requires an InnoDB/MyISAM table, FULLTEXT-compatible text columns and index creation privileges.";
    protected override string Quote(string identifier) => "`" + identifier.Replace("`", "``") + "`";
    protected override string SearchText(string[] terms) => string.Join(" ", terms.Select(term => term + "*"));

    protected override void EnsureIndex(DbConnection connection, FullTextTable table)
    {
        var schema = table.NameParts.Length == 2 ? table.NameParts[0] : Convert.ToString(Scalar(connection, "SELECT DATABASE()"));
        using var command = Command(connection,
            "SELECT INDEX_NAME,COLUMN_NAME,SEQ_IN_INDEX FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table AND INDEX_TYPE='FULLTEXT' ORDER BY INDEX_NAME,SEQ_IN_INDEX",
            new() { ["@schema"] = schema!, ["@table"] = table.NameParts[^1] });
        var indexes = new Dictionary<string, List<string>>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (!indexes.TryGetValue(name, out var columns))
                    indexes[name] = columns = [];
                columns.Add(reader.GetString(1));
            }
        }
        if (indexes.Values.Any(columns => columns.SequenceEqual(table.TextColumns, StringComparer.OrdinalIgnoreCase)))
            return;
        Run(connection, $"CREATE FULLTEXT INDEX {Quote(table.IndexName)} ON {Name(table)} ({string.Join(",", table.TextColumns.Select(Quote))})");
    }

    internal override string Predicate(FullTextTable table, string parameter) => Rank(table, parameter) + ">0";
    internal override string Rank(FullTextTable table, string parameter) =>
        $"MATCH({string.Join(",", table.TextColumns.Select(column => Column(table, column)))}) AGAINST ({parameter} IN BOOLEAN MODE)";
}
