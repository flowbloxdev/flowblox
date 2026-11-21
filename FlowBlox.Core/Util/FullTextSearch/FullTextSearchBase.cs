using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FlowBlox.Core.Enums;

namespace FlowBlox.Core.Util.FullTextSearch;

/// <summary>
/// Enables native indexing and portable FULLTEXT_MATCH/FULLTEXT_RANK expressions on existing SQL connections.
/// </summary>
internal abstract class FullTextSearchBase
{
    private static readonly ConditionalWeakTable<DbConnection, Dictionary<string, FullTextTable>> Tables = new();
    protected abstract DbTypes Provider { get; }

    public static FullTextSearchBase For(DbTypes provider) => provider switch
    {
        DbTypes.SQLite => new SqliteFullTextSearch(),
        DbTypes.MSSQL => new MsSqlFullTextSearch(),
        DbTypes.MySQL => new MySqlFullTextSearch(),
        DbTypes.Oracle => new OracleFullTextSearch(),
        _ => throw new NotSupportedException($"Native full-text search is not supported for {provider}.")
    };

    public DataTable Read(string statement, Dictionary<string, object> parameters, DbConnection connection)
    {
        SqlStatementSafetyGuard.EnsureAllowed(statement);
        var query = new FullTextSql(statement);
        if (!query.IsSelect)
            throw new NotSupportedException("A full-text SQLTable requires a single-table SELECT query.");
        lock (connection)
        {
            var table = EnsureTable(connection, query);
            var sql = PrepareQuery(query, table, parameters);
            if (query.SearchArgument != null)
                BeforeSearch(connection, table);
            using var command = Command(connection, sql, parameters);
            using var reader = command.ExecuteReader();
            var result = new DataTable();
            result.Load(reader);
            return result;
        }
    }

    public int Execute(string statement, Dictionary<string, object> parameters, DbConnection connection)
    {
        SqlStatementSafetyGuard.EnsureAllowed(statement);
        var query = new FullTextSql(statement);
        lock (connection)
        {
            if (query.IsCreate)
            {
                var affected = Run(connection, statement, parameters);
                EnsureTable(connection, query);
                return affected;
            }
            var table = EnsureTable(connection, query);
            return Run(connection, PrepareQuery(query, table, parameters), parameters);
        }
    }

    private FullTextTable EnsureTable(DbConnection connection, FullTextSql query)
    {
        var cache = Tables.GetOrCreateValue(connection);
        var parts = query.TableParts.Select((part, index) => NormalizeIdentifier(part, query.QuotedTableParts[index])).ToArray();
        var searchColumns = query.SearchColumns.Select((part, index) => NormalizeIdentifier(part, query.QuotedSearchColumns[index])).ToArray();
        var key = Provider + "|" + string.Join(".", parts) + "|" + string.Join("|", searchColumns);
        if (!cache.TryGetValue(key, out var cached))
        {
            var table = new FullTextTable { NameParts = parts };
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM " + Name(table) + " WHERE 1=0";
                using (var reader = command.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo))
                    table.Columns = reader.GetColumnSchema().ToList();
                table.TextColumns = table.Columns
                    .Where(column => column.DataType == typeof(string) && column.IsHidden != true)
                    .Select(column => column.BaseColumnName ?? column.ColumnName!)
                    .ToList();
                if (searchColumns.Length > 0)
                {
                    var comparer = Provider == DbTypes.Oracle ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
                    table.TextColumns = searchColumns.Select(column => table.TextColumns.FirstOrDefault(name => comparer.Equals(name, column))
                        ?? throw new NotSupportedException($"Full-text column '{column}' is missing or is not a text column."))
                        .Distinct(comparer)
                        .ToList();
                }
                if (table.TextColumns.Count == 0)
                    throw new NotSupportedException("The table has no searchable text columns.");
                var signature = key + "|" + string.Join("|", table.TextColumns);
                table.IndexName = "fbx_fts_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..16].ToLowerInvariant();
                EnsureIndex(connection, table);
            }
            catch (DbException exception)
            {
                throw new NotSupportedException(
                    $"Native {Provider} full-text indexing is unavailable for {Name(table)}. {Prerequisites} Database error: {exception.Message}", exception);
            }
            cache[key] = table;
            cached = table;
        }
        return new FullTextTable
        {
            NameParts = cached.NameParts,
            Alias = NormalizeIdentifier(query.Alias, query.QuotedAlias),
            Columns = cached.Columns,
            TextColumns = cached.TextColumns,
            KeyColumn = cached.KeyColumn,
            KeyIndex = cached.KeyIndex,
            IndexName = cached.IndexName
        };
    }

    internal string PrepareQuery(FullTextSql query, FullTextTable table, Dictionary<string, object> parameters)
    {
        if (query.SearchArgument == null)
            return query.Statement;
        var argument = query.SearchArgument;
        object? value;
        if (argument.StartsWith('\''))
            value = argument[1..^1].Replace("''", "'");
        else if (!parameters.TryGetValue(argument, out value))
            throw new ArgumentException("FULLTEXT_MATCH requires an existing bound SQL parameter.");
        var terms = FullTextSql.Terms(value == DBNull.Value ? string.Empty : Convert.ToString(value) ?? string.Empty);
        if (terms.Length == 0)
            return query.Rewrite("1=0", "(0+0)");
        var parameter = Provider == DbTypes.Oracle ? ":fbx_search" : "@fbx_search";
        while (parameters.ContainsKey(parameter))
            parameter += "_";
        parameters[parameter] = SearchText(terms);
        return query.Rewrite(Predicate(table, parameter), Rank(table, parameter));
    }

    protected virtual string SearchText(string[] terms) => string.Join(" ", terms);
    protected abstract string Prerequisites { get; }
    protected abstract void EnsureIndex(DbConnection connection, FullTextTable table);
    protected virtual void BeforeSearch(DbConnection connection, FullTextTable table) { }
    internal abstract string Predicate(FullTextTable table, string parameter);
    internal abstract string Rank(FullTextTable table, string parameter);
    protected abstract string Quote(string identifier);
    protected virtual string NormalizeIdentifier(string name, bool quoted) => name;
    protected string Name(FullTextTable table) => string.Join(".", table.NameParts.Select(Quote));
    protected string Column(FullTextTable table, string name) => Quote(table.Alias) + "." + Quote(name);

    protected static int Run(DbConnection connection, string sql, Dictionary<string, object>? parameters = null, DbTransaction? transaction = null)
    {
        using var command = Command(connection, sql, parameters, transaction);
        return command.ExecuteNonQuery();
    }

    protected static object? Scalar(DbConnection connection, string sql, Dictionary<string, object>? parameters = null)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteScalar();
    }

    protected static DbCommand Command(DbConnection connection, string sql, Dictionary<string, object>? parameters = null, DbTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        if (command is Oracle.ManagedDataAccess.Client.OracleCommand oracle)
            oracle.BindByName = true;
        foreach (var pair in parameters ?? [])
        {
            if (!Regex.IsMatch(FullTextSql.Mask(sql), Regex.Escape(pair.Key) + @"(?![\w])"))
                continue;
            var parameter = command.CreateParameter();
            parameter.ParameterName = command is Oracle.ManagedDataAccess.Client.OracleCommand ? pair.Key.TrimStart(':') : pair.Key;
            parameter.Value = pair.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}
