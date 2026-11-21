using System.Data.Common;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Util.FullTextSearch;

internal sealed class SqliteFullTextSearch : FullTextSearchBase
{
    protected override DbTypes Provider => DbTypes.SQLite;
    protected override string Prerequisites => "SQLite must include FTS5 and allow creation of virtual tables and synchronization triggers.";
    protected override string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    protected override string SearchText(string[] terms) => string.Join(" OR ", terms.Select(term => "\"" + term + "\""));

    protected override void EnsureIndex(DbConnection connection, FullTextTable table)
    {
        if (table.NameParts.Length > 1 && !table.NameParts[0].Equals("main", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite full-text indexing currently requires a table in the main database.");
        var definition = Convert.ToString(Scalar(connection, "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name",
            new() { ["@name"] = table.NameParts[^1] })) ?? string.Empty;
        if (definition.Contains("WITHOUT ROWID", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("SQLite FTS5 requires a rowid table; WITHOUT ROWID tables are not supported.");
        table.KeyColumn = new[] { "rowid", "_rowid_", "oid" }
            .FirstOrDefault(alias => table.Columns.All(column => !string.Equals(column.ColumnName, alias, StringComparison.OrdinalIgnoreCase)))
            ?? throw new NotSupportedException("SQLite FTS5 requires an accessible rowid alias.");
        if (Scalar(connection, "SELECT 1 FROM sqlite_master WHERE name=@name", new() { ["@name"] = table.IndexName }) != null)
            return;

        var index = Quote(table.IndexName);
        var columns = string.Join(",", table.TextColumns.Select(Quote));
        var source = Quote(table.NameParts[^1]);
        var old = string.Join(",", table.TextColumns.Select(column => "old." + Quote(column)));
        var updated = string.Join(",", table.TextColumns.Select(column => "new." + Quote(column)));
        var key = Quote(table.KeyColumn);
        using var transaction = connection.BeginTransaction();
        // Only FlowBlox-owned FTS storage is changed by these triggers; user SQL retains its normal safety checks.
        Run(connection, $"CREATE VIRTUAL TABLE {index} USING fts5({columns},content='{table.NameParts[^1].Replace("'", "''")}',content_rowid='{table.KeyColumn}');", transaction: transaction);
        Run(connection, $"CREATE TRIGGER {Quote(table.IndexName + "_ai")} AFTER INSERT ON {source} BEGIN INSERT INTO {index}(rowid,{columns}) VALUES(new.{key},{updated}); END;", transaction: transaction);
        Run(connection, $"CREATE TRIGGER {Quote(table.IndexName + "_ad")} AFTER DELETE ON {source} BEGIN INSERT INTO {index}({index},rowid,{columns}) VALUES('delete',old.{key},{old}); END;", transaction: transaction);
        Run(connection, $"CREATE TRIGGER {Quote(table.IndexName + "_au")} AFTER UPDATE ON {source} BEGIN INSERT INTO {index}({index},rowid,{columns}) VALUES('delete',old.{key},{old}); INSERT INTO {index}(rowid,{columns}) VALUES(new.{key},{updated}); END;", transaction: transaction);
        Run(connection, $"INSERT INTO {index}({index}) VALUES('rebuild')", transaction: transaction);
        transaction.Commit();
    }

    internal override string Predicate(FullTextTable table, string parameter) =>
        $"{Column(table, table.KeyColumn)} IN (SELECT rowid FROM {Quote(table.IndexName)} WHERE {Quote(table.IndexName)} MATCH {parameter})";

    internal override string Rank(FullTextTable table, string parameter) =>
        $"(SELECT -bm25({Quote(table.IndexName)}) FROM {Quote(table.IndexName)} WHERE {Quote(table.IndexName)} MATCH {parameter} AND rowid={Column(table, table.KeyColumn)})";
}
