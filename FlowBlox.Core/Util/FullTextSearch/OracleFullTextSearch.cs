using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Util.FullTextSearch;

internal sealed class OracleFullTextSearch : FullTextSearchBase
{
    protected override DbTypes Provider => DbTypes.Oracle;
    protected override string Prerequisites => "Oracle Text (CTXSYS.CONTEXT) must be installed; the account needs text-index creation privileges on the source table.";
    protected override string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    protected override string NormalizeIdentifier(string name, bool quoted) => quoted ? name : name.ToUpperInvariant();
    protected override string SearchText(string[] terms) => string.Join(" | ", terms.Select(term => "{" + term + "}"));

    protected override void EnsureIndex(DbConnection connection, FullTextTable table)
    {
        var schema = table.NameParts.Length == 2 ? table.NameParts[0]
            : Convert.ToString(Scalar(connection, "SELECT SYS_CONTEXT('USERENV','CURRENT_SCHEMA') FROM dual"));
        for (var index = 0; index < table.TextColumns.Count; index++)
        {
            var signature = Name(table) + "|" + table.TextColumns[index];
            var name = "fbx_fts_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..16].ToLowerInvariant();
            if (Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM all_indexes WHERE owner=:owner AND index_name=:name",
                new() { [":owner"] = schema!, [":name"] = name })) > 0)
                continue;
            var qualifiedIndex = schema == null ? Quote(name) : Quote(schema) + "." + Quote(name);
            Run(connection, $"CREATE INDEX {qualifiedIndex} ON {Name(table)} ({Quote(table.TextColumns[index])}) INDEXTYPE IS CTXSYS.CONTEXT PARAMETERS ('SYNC (ON COMMIT) TRANSACTIONAL')");
        }
    }

    internal override string Predicate(FullTextTable table, string parameter) => "(" + string.Join(" OR ",
        table.TextColumns.Select((column, index) => $"CONTAINS({Column(table, column)},{parameter},{index + 1})>0")) + ")";
    internal override string Rank(FullTextTable table, string parameter) => "(" + string.Join("+",
        table.TextColumns.Select((_, index) => $"SCORE({index + 1})")) + ")";
}
