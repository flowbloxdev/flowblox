using System.Data;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Persistence;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.FullTextSearch;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace FlowBloxTest.Util;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class NativeFullTextSearchTests
{
    private SqliteConnection _connection = null!;
    private FullTextSearchBase _search = null!;
    private FlowBloxProject? _previousProject;

    [TestInitialize]
    public void Initialize()
    {
        _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _search = FullTextSearchBase.For(DbTypes.SQLite);
        Raw("CREATE TABLE documents(Id INTEGER PRIMARY KEY,Title TEXT,FileName TEXT,Content TEXT,Category TEXT)");
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Dispose();
        FlowBloxProjectManager.Instance.ActiveProject = _previousProject;
    }

    [TestMethod]
    public void FirstSearch_BackfillsExistingRowsAndMatchesFilename()
    {
        Raw("INSERT INTO documents VALUES(1,'Invoice','quartz_2026.pdf','Annual statement','files')");
        var result = Search("quartz");
        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual(1L, result.Rows[0]["Id"]);
        Assert.IsTrue(Convert.ToDouble(result.Rows[0]["Score"]) > 0);
    }

    [TestMethod]
    public void Triggers_KeepIndexCurrentForExternalInsertUpdateAndDelete()
    {
        Search("quartz");
        Raw("INSERT INTO documents VALUES(1,'quartz','one.txt','first','files')");
        Assert.AreEqual(1, Search("quartz").Rows.Count);
        Raw("UPDATE documents SET Title='cobalt',Content='changed' WHERE Id=1");
        Assert.AreEqual(0, Search("quartz").Rows.Count);
        Assert.AreEqual(1, Search("cobalt").Rows.Count);
        Raw("DELETE FROM documents WHERE Id=1");
        Assert.AreEqual(0, Search("cobalt").Rows.Count);
    }

    [TestMethod]
    public void RankingAndLimit_AreAppliedBeforeReturningRows()
    {
        Raw("INSERT INTO documents VALUES(1,'quartz quartz quartz','a.txt','quartz quartz','files')");
        Raw("INSERT INTO documents VALUES(2,'Other','b.txt','A longer document containing quartz and many unrelated words about other items','files')");
        var result = Search("quartz", 1);
        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual(1L, result.Rows[0]["Id"]);
    }

    [TestMethod]
    public void OrdinaryFilters_ArePreservedAlongsideRankOrdering()
    {
        Raw("INSERT INTO documents VALUES(1,'quartz quartz','a.txt','quartz','private'),(2,'quartz','b.txt','public','public')");
        var result = _search.Read(
            "SELECT d.Id,d.Title,FULLTEXT_RANK() AS Score FROM documents d WHERE d.Category=@category AND FULLTEXT_MATCH(@query) ORDER BY d.Id DESC LIMIT @limit",
            new() { ["@query"] = "quartz", ["@category"] = "public", ["@limit"] = 2 }, _connection);
        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual(2L, result.Rows[0]["Id"]);
    }

    [TestMethod]
    public void SearchInput_CannotExecuteSqlOrFtsOperators()
    {
        Raw("INSERT INTO documents VALUES(1,'safe','a.txt','ordinary text','files')");
        Assert.AreEqual(0, Search("\"' ); DROP TABLE documents; -- NEAR(**) OR *").Rows.Count);
        Assert.AreEqual(1, Search("safe").Rows.Count);
    }

    [TestMethod]
    public void EmptySearch_ReturnsNoRows()
    {
        Raw("INSERT INTO documents VALUES(1,'safe','a.txt','ordinary text','files')");
        Assert.AreEqual(0, Search(" \" -- * ").Rows.Count);
    }

    [TestMethod]
    public void PortableFunctionInLiteral_RemainsLiteral()
    {
        Raw("INSERT INTO documents VALUES(1,'safe','a.txt','ordinary text','files')");
        var result = _search.Read("SELECT 'FULLTEXT_MATCH(@query)' AS Literal,Title FROM documents", [], _connection);
        Assert.AreEqual("FULLTEXT_MATCH(@query)", result.Rows[0]["Literal"]);
    }

    [TestMethod]
    [DataRow("SELECT * FROM documents a JOIN documents b ON a.Id=b.Id WHERE FULLTEXT_MATCH(@q)")]
    [DataRow("SELECT * FROM documents WHERE FULLTEXT_MATCH(@q); SELECT 1")]
    [DataRow("SELECT * FROM documents WHERE FULLTEXT_MATCH(Content,COUNT(*),@q)")]
    public void UnsupportedQueryShape_IsRejectedBeforeIndexCreation(string sql)
    {
        Assert.ThrowsExactly<NotSupportedException>(() => _search.Read(sql, new() { ["@q"] = "safe" }, _connection));
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE 'fbx_fts_%'";
        Assert.AreEqual(0L, command.ExecuteScalar());
    }

    [TestMethod]
    public void QuotedReservedIdentifiers_AreNotParsedAsSqlClauses()
    {
        Raw("INSERT INTO documents VALUES(1,'quartz','a.txt','text','files')");
        var result = _search.Read("SELECT [order].Id FROM [documents] AS [order] WHERE FULLTEXT_MATCH([Title],@q) ORDER BY [order].Id",
            new() { ["@q"] = "quartz" }, _connection);
        Assert.AreEqual(1, result.Rows.Count);
    }

    [TestMethod]
    public void ExplicitSearchColumns_ExcludeUnrelatedTextMetadata()
    {
        Raw("INSERT INTO documents VALUES(1,'ordinary','a.txt','text','quartz'),(2,'quartz','b.txt','text','files')");
        var result = _search.Read("SELECT Id,Title FROM documents WHERE FULLTEXT_MATCH(Title,FileName,Content,@q)",
            new() { ["@q"] = "quartz" }, _connection);
        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual(2L, result.Rows[0]["Id"]);
    }

    [TestMethod]
    public void CreateAndUpsertThroughExecutor_AreSearchableWithoutManualIndexSql()
    {
        _search.Execute("CREATE TABLE IF NOT EXISTS extra(Id INTEGER PRIMARY KEY,Text TEXT)", [], _connection);
        _search.Execute("INSERT INTO extra VALUES(@id,@text) ON CONFLICT(Id) DO UPDATE SET Text=excluded.Text",
            new() { ["@id"] = 1, ["@text"] = "quartz" }, _connection);
        _search.Execute("INSERT INTO extra VALUES(@id,@text) ON CONFLICT(Id) DO UPDATE SET Text=excluded.Text",
            new() { ["@id"] = 1, ["@text"] = "cobalt" }, _connection);
        var result = _search.Read("SELECT * FROM extra WHERE FULLTEXT_MATCH(@q)", new() { ["@q"] = "cobalt" }, _connection);
        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual("cobalt", result.Rows[0]["Text"]);
    }

    [TestMethod]
    public void SerializedOptIn_EnablesNativeSearchWhileLegacyQueryKeepsItsFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), "fbx-fulltext-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var connectionString = "Data Source=" + path;
        try
        {
            var setup = new ExecuteSQLFlowBlock
            {
                DbType = DbTypes.SQLite,
                SQLConnectionstring = connectionString,
                SQLStatement = "CREATE TABLE documents(Id INTEGER PRIMARY KEY,Title TEXT)",
                EnableFullTextSearch = true
            };
            JsonConvert.DeserializeObject<ExecuteSQLFlowBlock>(JsonConvert.SerializeObject(setup))!.ExecuteSQL();
            setup.SQLStatement = "INSERT INTO documents VALUES(1,'quartz')";
            setup.ExecuteSQL();
            var table = new SQLTable
            {
                DbType = DbTypes.SQLite,
                SQLConnectionstring = connectionString,
                SQLStatement = "SELECT Title FROM documents WHERE FULLTEXT_MATCH('quartz')",
                EnableFullTextSearch = true
            };
            var result = JsonConvert.DeserializeObject<SQLTable>(JsonConvert.SerializeObject(table))!.Read();
            Assert.AreEqual("quartz", result.Rows[0]["Title"]);
            table.EnableFullTextSearch = false;
            table.SQLStatement = "SELECT Title FROM documents WHERE Title='quartz'";
            var legacy = JsonConvert.DeserializeObject<SQLTable>(JsonConvert.SerializeObject(table))!.Read();
            Assert.AreEqual(1, legacy.Columns.Count);
            Assert.AreEqual("quartz", legacy.Rows[0]["Title"]);
        }
        finally
        {
            DbConnectionProvider.Instance.ReleaseSqliteConnections(path);
            File.Delete(path);
        }
    }

    private DataTable Search(string text, int limit = 20) => _search.Read(
        "SELECT d.Id,d.Title,d.FileName,d.Content,FULLTEXT_RANK() AS Score FROM documents AS d WHERE FULLTEXT_MATCH(@query) LIMIT @limit",
        new() { ["@query"] = text, ["@limit"] = limit }, _connection);

    private void Raw(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
