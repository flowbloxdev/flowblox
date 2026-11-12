using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace FlowBlox.Core.Provider;

internal static class SqliteConnectionHelper
{
    /// <summary>
    /// Releases cached connections and pools for a SQLite file, including equivalent connection strings.
    /// </summary>
    internal static void ReleaseConnections(IDictionary<string, DbConnection> connections, string filePath)
    {
        filePath = Path.GetFullPath(filePath);
        foreach (var entry in connections.Where(entry => UsesSqliteFile(entry.Value, filePath)).ToArray())
        {
            var connection = (SqliteConnection)entry.Value;
            connection.Close();
            SqliteConnection.ClearPool(connection);
            connection.Dispose();
            connections.Remove(entry.Key);
        }
    }

    private static bool UsesSqliteFile(DbConnection connection, string filePath)
    {
        var sqlite = connection as SqliteConnection;
        if (sqlite == null || string.IsNullOrWhiteSpace(sqlite.DataSource))
            return false;

        var options = new SqliteConnectionStringBuilder(sqlite.ConnectionString);
        if (options.Mode == SqliteOpenMode.Memory || options.DataSource == ":memory:")
            return false;

        return string.Equals(
            Path.GetFullPath(sqlite.DataSource),
            filePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
