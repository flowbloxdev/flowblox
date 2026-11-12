using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.Components.Modifier;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Logic;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.FlowBlocks.Persistence;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using Microsoft.Data.Sqlite;

namespace FlowBloxTest.FlowBlocks.IO;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class LocalFileProcessingTests : FlowBloxTestsBase
{
    private FlowBloxProject _project = null!;
    private IDisposable _scope = null!;
    private string _directory = null!;
    private string DatabasePath => Path.Combine(_directory, "hash store; local.sqlite");

    [TestInitialize]
    public void Initialize()
    {
        _project = new FlowBloxProject();
        _scope = FlowBloxRegistryProvider.BeginScopedRegistry(_project.FlowBloxRegistry);
        _directory = Path.Combine(Path.GetTempPath(), "FlowBloxLocalFiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        CreateFlowBlock<StartFlowBlock>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        DbConnectionProvider.Instance.ReleaseSqliteConnections(DatabasePath);
        _scope.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public void LocalDatabase_UsesExistingSqlConsumersAndIgnoresDuplicateHashes()
    {
        var creator = CreateDatabaseCreator();
        Assert.IsTrue(creator.Execute(new TransientRuntime(_project), null!));
        var connectionString = creator.GridElementResult.Results.Single().FieldValueMappings.Single().Value;
        var execute = new ExecuteSQLFlowBlock
        {
            DbType = DbTypes.SQLite,
            SQLConnectionstring = connectionString,
            SQLStatement = "CREATE TABLE FileHashes (Hash TEXT PRIMARY KEY, SourcePath TEXT NOT NULL);"
        };
        execute.ExecuteSQL();

        var hash = Registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Hash");
        hash.StringValue = "hash'; DROP TABLE FileHashes; --";
        var path = Registry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Text, "Path");
        path.StringValue = "C:\\Files\\Ada's report.bin";
        execute.SQLStatement = $"INSERT OR IGNORE INTO FileHashes (Hash, SourcePath) VALUES ({hash.FullyQualifiedName}, {path.FullyQualifiedName});";
        execute.ExecuteSQL();
        execute.ExecuteSQL();

        var table = new SQLTable
        {
            DbType = DbTypes.SQLite,
            SQLConnectionstring = connectionString,
            SQLStatement = $"SELECT SourcePath FROM FileHashes WHERE Hash = {hash.FullyQualifiedName};"
        };
        var rows = table.Read();
        Assert.HasCount(1, rows.Rows);
        Assert.AreEqual(path.StringValue, rows.Rows[0]["SourcePath"]);
    }

    [TestMethod]
    public void LocalDatabase_ConnectionStringFeedsSqlFlowAtRuntime()
    {
        var creator = CreateDatabaseCreator();
        creator.ReferencedFlowBlocks.Add(Registry.GetFlowBlocks<StartFlowBlock>().Single());
        var schema = CreateFlowBlock<ExecuteSQLFlowBlock>(creator);
        schema.DbType = DbTypes.SQLite;
        schema.SQLConnectionstring = creator.ResultField.FullyQualifiedName;
        schema.SQLStatement = "CREATE TABLE FileHashes (Hash TEXT PRIMARY KEY, SourcePath TEXT NOT NULL);";

        CreateRuntimeAndExecute(_project);

        Assert.AreEqual("true", schema.GridElementResult.Results.Single().FieldValueMappings.Single().Value);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM FileHashes;";
        Assert.AreEqual(0L, command.ExecuteScalar());
        SqliteConnection.ClearPool(connection);
    }

    [TestMethod]
    public void LocalDatabase_DirectoryTargetIsPreservedAndProducesHandledFailure()
    {
        var creator = CreateDatabaseCreator();
        creator.DatabaseFile.FilePath = _directory;
        var errors = 0;
        creator.OnError += (_, _) => errors++;

        Assert.IsTrue(creator.Execute(new TransientRuntime(_project), null!));
        Assert.AreEqual(1, errors);
        Assert.IsTrue(Directory.Exists(_directory));
        Assert.IsNull(creator.GridElementResult.Results.Single().FieldValueMappings.Single().Value);
    }

    [TestMethod]
    public void RecreateDatabase_ReleasesEquivalentCachedConnectionsAndRemovesOldSchema()
    {
        var creator = CreateDatabaseCreator();
        var runtime = new TransientRuntime(_project);
        Assert.IsTrue(creator.Execute(runtime, null!));
        var connectionString = creator.GridElementResult.Results.Single().FieldValueMappings.Single().Value;
        var old = DbConnectionProvider.Instance.GetOrCreateDbConnection(DbTypes.SQLite, connectionString);
        using (var command = old.CreateCommand())
        {
            command.CommandText = "CREATE TABLE PreviousRun (Value TEXT);";
            command.ExecuteNonQuery();
        }

        var equivalent = new SqliteConnectionStringBuilder(connectionString) { Cache = SqliteCacheMode.Private }.ConnectionString;
        DbConnectionProvider.Instance.GetOrCreateDbConnection(DbTypes.SQLite, equivalent);
        Assert.IsTrue(creator.Execute(runtime, null!));
        var current = DbConnectionProvider.Instance.GetOrCreateDbConnection(DbTypes.SQLite, equivalent);
        Assert.AreNotSame(old, current);
        using var check = current.CreateCommand();
        check.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table';";
        Assert.AreEqual(0L, check.ExecuteScalar());
    }

    [TestMethod]
    [DataRow("AAH/", "AAH/", true)]
    [DataRow("AAH/", "AAEA", false)]
    [DataRow("", "", true)]
    public void FileReader_HashesContentOnATextCopyWithoutChangingBinaryResults(string firstContent, string secondContent, bool equal)
    {
        var start = Registry.GetFlowBlocks<StartFlowBlock>().Single();
        var first = CreateBinaryFileReader("first.bin", firstContent, start);
        var second = CreateBinaryFileReader("other name.bin", secondContent, first);
        var firstHash = CreateFlowBlock<FieldCopyFlowBlock>(second);
        firstHash.InputField = first.ResultField;
        firstHash.RequiredFields.Clear();
        var firstHashField = Registry.CreateField(firstHash);
        firstHashField.Modifiers.Add(new HashModifier());
        firstHash.CopiedFields.Add(firstHashField);
        var secondHash = CreateFlowBlock<FieldCopyFlowBlock>(firstHash);
        secondHash.InputField = second.ResultField;
        secondHash.RequiredFields.Clear();
        var secondHashField = Registry.CreateField(secondHash);
        secondHashField.Modifiers.Add(new HashModifier());
        secondHash.CopiedFields.Add(secondHashField);

        CreateRuntimeAndExecute(_project);

        var hash = firstHash.GridElementResult.Results.Single().FieldValueMappings.Single().Value;
        var otherHash = secondHash.GridElementResult.Results.Single().FieldValueMappings.Single().Value;
        Assert.IsFalse(string.IsNullOrEmpty(hash));
        Assert.AreEqual(equal, hash == otherHash);
        Assert.AreEqual(firstContent, first.GridElementResult.Results.Single().FieldValueMappings.Single().Value);
        Assert.AreEqual(secondContent, second.GridElementResult.Results.Single().FieldValueMappings.Single().Value);
    }

    [TestMethod]
    [DataRow("", "", "true")]
    [DataRow("AAE=", "AAE=", "true")]
    [DataRow("AAE=", "AQE=", "false")]
    [DataRow("AAE=", "AAEC", "false")]
    public void BinaryComparison_ReadsActualFileReaderResults(string firstContent, string secondContent, string expected)
    {
        var start = Registry.GetFlowBlocks<StartFlowBlock>().Single();
        var first = CreateBinaryFileReader("first.bin", firstContent, start);
        var second = CreateBinaryFileReader("second.bin", secondContent, first);
        var compare = CreateFlowBlock<DecisionFlowBlock>(second);
        compare.FallbackValue = "false";
        compare.Decisions.Add(new FieldComparisonCondition
        {
            FieldElement = first.ResultField,
            Operator = ComparisonOperator.Equals,
            Value = second.ResultField.FullyQualifiedName,
            OutputValue = "true"
        });

        CreateRuntimeAndExecute(_project);
        Assert.AreEqual(expected, compare.GridElementResult.Results.Single().FieldValueMappings.Single().Value);
    }

    private CreateSqlLiteDbFlowBlock CreateDatabaseCreator()
    {
        var block = CreateFlowBlock<CreateSqlLiteDbFlowBlock>();
        block.DatabaseFile = new FileObject { FilePath = DatabasePath };
        return block;
    }

    private FileReaderFlowBlock CreateBinaryFileReader(string name, string content, BaseFlowBlock previous)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, Convert.FromBase64String(content));
        var reader = CreateFlowBlock<FileReaderFlowBlock>(previous);
        reader.ResultFieldFieldType = FieldTypes.ByteArray;
        reader.DataSource = new FileObject { FilePath = path };
        return reader;
    }
}
