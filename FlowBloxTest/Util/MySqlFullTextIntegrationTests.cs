using FlowBlox.Core.Enums;
using FlowBlox.Core.Util.FullTextSearch;
using MySql.Data.MySqlClient;

namespace FlowBloxTest.Util;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.IntegrationTests)]
public sealed class MySqlFullTextIntegrationTests
{
    [TestMethod]
    public void NativeIndex_TracksWritesAndRanksFilenameAndContent()
    {
        if (!uint.TryParse(Environment.GetEnvironmentVariable("FLOWBLOX_FTS_MYSQL_TEST_PORT"), out var port) || port == 3306)
            Assert.Inconclusive("Set FLOWBLOX_FTS_MYSQL_TEST_PORT to an isolated loopback test instance, not the working database.");
        var schema = "flowblox_fts_test_" + Guid.NewGuid().ToString("N");
        var configuration = new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1", Port = port, UserID = "root", Password = "", SslMode = MySqlSslMode.Disabled
        };
        using var server = new MySqlConnection(configuration.ConnectionString);
        server.Open();
        Execute(server, $"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4");
        try
        {
            configuration.Database = schema;
            using var connection = new MySqlConnection(configuration.ConnectionString);
            connection.Open();
            Execute(connection, "CREATE TABLE documents(Id INT PRIMARY KEY,Title VARCHAR(255),FileName VARCHAR(255),Content TEXT,Category VARCHAR(32)) ENGINE=InnoDB");
            Execute(connection, "INSERT INTO documents VALUES(1,'ordinary','quartz_2026.txt','text','private'),(2,'cobalt','two.txt','cobalt text','public')");
            var search = FullTextSearchBase.For(DbTypes.MySQL);
            var sql = "SELECT Id,Title,FULLTEXT_RANK() AS Score FROM documents WHERE FULLTEXT_MATCH(Title,FileName,Content,@q) LIMIT 10";
            var result = search.Read(sql, new() { ["@q"] = "quartz" }, connection);
            Assert.AreEqual(1, result.Rows.Count);
            Assert.AreEqual(1, Convert.ToInt32(result.Rows[0]["Id"]));
            Execute(connection, "UPDATE documents SET FileName='other.txt',Title='cobalt' WHERE Id=1");
            Assert.AreEqual(0, search.Read(sql, new() { ["@q"] = "quartz" }, connection).Rows.Count);
            Assert.AreEqual(2, search.Read(sql, new() { ["@q"] = "cobalt" }, connection).Rows.Count);
            Execute(connection, "DELETE FROM documents WHERE Id=2");
            Assert.AreEqual(1, search.Read(sql, new() { ["@q"] = "cobalt" }, connection).Rows.Count);
        }
        finally
        {
            MySqlConnection.ClearAllPools();
            Execute(server, $"DROP DATABASE `{schema}`");
        }
    }

    private static void Execute(MySqlConnection connection, string sql)
    {
        using var command = new MySqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
