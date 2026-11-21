using System.Data.Common;
using System.Diagnostics;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Util.FullTextSearch;

internal sealed class MsSqlFullTextSearch : FullTextSearchBase
{
    protected override DbTypes Provider => DbTypes.MSSQL;
    protected override string Prerequisites => "SQL Server requires the Full-Text Search feature, a single-column unique non-null key index and full-text/catalog creation privileges.";
    protected override string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    protected override void EnsureIndex(DbConnection connection, FullTextTable table)
    {
        if (Convert.ToInt32(Scalar(connection, "SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')")) != 1)
            throw new NotSupportedException(Prerequisites);
        var parameters = new Dictionary<string, object> { ["@table"] = Name(table) };
        using var command = Command(connection, """
            SELECT TOP (1) i.name,c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE i.object_id=OBJECT_ID(@table) AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0 AND c.is_nullable=0
              AND (SELECT COUNT(*) FROM sys.index_columns k WHERE k.object_id=i.object_id AND k.index_id=i.index_id AND k.key_ordinal>0)=1
            ORDER BY CASE WHEN i.index_id=(SELECT unique_index_id FROM sys.fulltext_indexes WHERE object_id=i.object_id) THEN 0 ELSE 1 END,i.is_primary_key DESC,i.index_id
            """, parameters);
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
                throw new NotSupportedException(Prerequisites);
            table.KeyIndex = reader.GetString(0);
            table.KeyColumn = reader.GetString(1);
        }
        if (Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM sys.fulltext_indexes WHERE object_id=OBJECT_ID(@table)", parameters)) > 0)
        {
            using var existing = Command(connection,
                "SELECT c.name FROM sys.fulltext_index_columns f JOIN sys.columns c ON c.object_id=f.object_id AND c.column_id=f.column_id WHERE f.object_id=OBJECT_ID(@table)", parameters);
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = existing.ExecuteReader())
                while (reader.Read())
                    columns.Add(reader.GetString(0));
            var missing = table.TextColumns.Where(column => !columns.Contains(column)).ToList();
            if (missing.Count > 0)
                Run(connection, $"ALTER FULLTEXT INDEX ON {Name(table)} ADD ({string.Join(",", missing.Select(column => Quote(column) + " LANGUAGE 0"))})");
            if (!string.Equals(Convert.ToString(Scalar(connection,
                "SELECT change_tracking_state_desc FROM sys.fulltext_indexes WHERE object_id=OBJECT_ID(@table)", parameters)), "AUTO", StringComparison.OrdinalIgnoreCase))
                Run(connection, $"ALTER FULLTEXT INDEX ON {Name(table)} SET CHANGE_TRACKING AUTO");
            if (Convert.ToInt32(Scalar(connection,
                "SELECT is_enabled FROM sys.fulltext_indexes WHERE object_id=OBJECT_ID(@table)", parameters)) == 0)
                Run(connection, $"ALTER FULLTEXT INDEX ON {Name(table)} ENABLE");
            return;
        }
        if (Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM sys.fulltext_catalogs WHERE name='fbx_fulltext'")) == 0)
            Run(connection, "CREATE FULLTEXT CATALOG [fbx_fulltext]");
        var columnsSql = string.Join(",", table.TextColumns.Select(column => Quote(column) + " LANGUAGE 0"));
        Run(connection, $"CREATE FULLTEXT INDEX ON {Name(table)} ({columnsSql}) KEY INDEX {Quote(table.KeyIndex)} ON [fbx_fulltext] WITH CHANGE_TRACKING AUTO");
    }

    private string Matches(FullTextTable table, string parameter) =>
        $"FREETEXTTABLE({Name(table)},({string.Join(",", table.TextColumns.Select(Quote))}),{parameter})";

    protected override void BeforeSearch(DbConnection connection, FullTextTable table)
    {
        var elapsed = Stopwatch.StartNew();
        var parameters = new Dictionary<string, object> { ["@table"] = Name(table) };
        while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
        {
            var pending = Convert.ToInt64(Scalar(connection,
                "SELECT COALESCE(CONVERT(bigint,OBJECTPROPERTYEX(OBJECT_ID(@table),'TableFulltextPendingChanges')),0)+COALESCE(CONVERT(bigint,OBJECTPROPERTYEX(OBJECT_ID(@table),'TableFulltextPopulateStatus')),0)+CASE WHEN EXISTS(SELECT 1 FROM sys.fulltext_indexes WHERE object_id=OBJECT_ID(@table) AND has_crawl_completed=0) THEN 1 ELSE 0 END", parameters));
            if (pending == 0)
                return;
            Thread.Sleep(100);
        }
        throw new TimeoutException("SQL Server full-text change tracking did not finish within 30 seconds. Check the full-text population status.");
    }
    internal override string Predicate(FullTextTable table, string parameter) =>
        $"{Column(table, table.KeyColumn)} IN (SELECT [KEY] FROM {Matches(table, parameter)})";
    internal override string Rank(FullTextTable table, string parameter) =>
        $"(SELECT [RANK] FROM {Matches(table, parameter)} AS fbx_match WHERE fbx_match.[KEY]={Column(table, table.KeyColumn)})";
}
