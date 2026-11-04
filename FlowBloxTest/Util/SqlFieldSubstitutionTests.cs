using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Persistence;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using Newtonsoft.Json;

namespace FlowBloxTest.Util;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class SqlFieldSubstitutionTests
{
    private FlowBloxProject? _previousProject;
    private FlowBloxProject _project = null!;

    [TestInitialize]
    public void Initialize()
    {
        _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        _project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = _project;
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowBloxProjectManager.Instance.ActiveProject = _previousProject;
    }

    [TestMethod]
    [DataRow(DbTypes.MSSQL, "@0")]
    [DataRow(DbTypes.MySQL, "@0")]
    [DataRow(DbTypes.SQLite, "@0")]
    [DataRow(DbTypes.Oracle, ":0")]
    public void DefaultMode_KeepsFieldContentAsDataAndReusesParameters(DbTypes dbType, string parameterName)
    {
        var field = Field("Name", "O'Brien; DROP TABLE Customers;");
        var sql = $"SELECT * FROM Customers WHERE Name = {field.FullyQualifiedName} OR Alias = {field.FullyQualifiedName}";

        var resolved = FlowBloxFieldHelper.ReplaceFieldsInSQL(sql, dbType, out var parameters);

        Assert.AreEqual($"SELECT * FROM Customers WHERE Name = {parameterName} OR Alias = {parameterName}", resolved);
        Assert.AreEqual(1, parameters.Count);
        Assert.AreEqual(field.StringValue, parameters[parameterName]);
    }

    [TestMethod]
    public void TextMode_ExpandsCompleteQueryWithoutCreatingADataParameter()
    {
        var field = Field("Sql", "SELECT 1 AS Test");

        var sql = FlowBloxFieldHelper.ReplaceFieldsInSQL(field.FullyQualifiedName, DbTypes.MSSQL, out var parameters, useFieldValuesAsParameters: false);

        Assert.AreEqual("SELECT 1 AS Test", sql);
        Assert.AreEqual(0, parameters.Count);
    }

    [TestMethod]
    public void TextMode_ExpandsIdentifiersAndFragmentsWithoutImplicitQuoting()
    {
        var column = Field("Column", "[Customer Name]");
        var table = Field("Table", "[dbo].[Customers]");

        var sql = FlowBloxFieldHelper.ReplaceFieldsInSQL($"SELECT {column.FullyQualifiedName} FROM {table.FullyQualifiedName}", DbTypes.MSSQL, out var parameters, useFieldValuesAsParameters: false);

        Assert.AreEqual("SELECT [Customer Name] FROM [dbo].[Customers]", sql);
        Assert.AreEqual(0, parameters.Count);
    }

    [TestMethod]
    public void ExistingSerializedComponents_KeepParameterizedBehaviorWhenSettingIsAbsent()
    {
        var field = Field("Sql", "SELECT 1 AS Test");
        var table = JsonConvert.DeserializeObject<SQLTable>("{\"SQLStatement\":\"$User::Sql\"}")!;
        var block = JsonConvert.DeserializeObject<ExecuteSQLFlowBlock>("{\"SQLStatement\":\"$User::Sql\"}")!;

        var tableSql = FlowBloxFieldHelper.ReplaceFieldsInSQL(table.SQLStatement, DbTypes.MSSQL, out var tableParameters, table.UseFieldValuesAsParameters);
        var blockSql = FlowBloxFieldHelper.ReplaceFieldsInSQL(block.SQLStatement, DbTypes.MSSQL, out var blockParameters, block.UseFieldValuesAsParameters);

        Assert.AreEqual("@0", tableSql);
        Assert.AreEqual("@0", blockSql);
        Assert.AreEqual(field.StringValue, tableParameters["@0"]);
        Assert.AreEqual(field.StringValue, blockParameters["@0"]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TextMode_DoesNotBypassDestructiveSqlGuard(bool executeNonQuery)
    {
        var option = FlowBloxOptions.GetOptionInstance().GetOption(SqlStatementSafetyGuard.AllowDestructiveStatementsOptionName);
        var previous = option.PersistentValue;
        option.PersistentValue = "false";
        try
        {
            var field = Field("Sql", "DROP TABLE Customers;");
            var sql = FlowBloxFieldHelper.ReplaceFieldsInSQL(field.FullyQualifiedName, DbTypes.MSSQL, out var parameters, useFieldValuesAsParameters: false);

            Assert.ThrowsExactly<NotSupportedException>(() =>
            {
                if (executeNonQuery)
                    DbConnectionUtil.ExecuteSqlQuery(sql, parameters, null!);
                else
                    DbConnectionUtil.GetOutputAsDataTable(sql, parameters, null!);
            });
        }
        finally
        {
            option.PersistentValue = previous;
        }
    }

    [TestMethod]
    public void ExecuteSqlTemplate_AllDatabasePathsResolveTheSuppliedQueryAsSqlText()
    {
        var project = Template("ExecuteSQLQuery");
        project.UserFields.Single(field => field.Name == "Input_SQLQuery").StringValue = "SELECT 1 AS Test";
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var tables = project.FlowBloxRegistry.GetManagedObjects<SQLTable>().ToList();

        Assert.AreEqual(4, tables.Count);
        foreach (var table in tables)
        {
            var sql = FlowBloxFieldHelper.ReplaceFieldsInSQL(table.SQLStatement, table.DbType, out var parameters, table.UseFieldValuesAsParameters);
            Assert.AreEqual("SELECT 1 AS Test", sql, table.Name);
            Assert.AreEqual(0, parameters.Count, table.Name);
        }
    }

    [TestMethod]
    public void MssqlColumnDiscovery_ContinuesToBindSchemaAndTableNameAsData()
    {
        var project = Template("MssqlColumnDiscovery");
        project.UserFields.Single(field => field.Name == "Input_MssqlSchema").StringValue = "dbo";
        project.UserFields.Single(field => field.Name == "Input_TableName").StringValue = "Customers";
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        var table = project.FlowBloxRegistry.GetManagedObjects<SQLTable>().Single();

        var sql = FlowBloxFieldHelper.ReplaceFieldsInSQL(table.SQLStatement, table.DbType, out var parameters, table.UseFieldValuesAsParameters);

        CollectionAssert.AreEquivalent(new[] { "dbo", "Customers" }, parameters.Values.Cast<string>().ToArray());
        Assert.IsFalse(sql.Contains("$User::", StringComparison.Ordinal));
    }

    private FieldElement Field(string name, string value)
    {
        var field = _project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Input, fieldName: name);
        field.StringValue = value;
        return field;
    }

    private static FlowBloxProject Template(string name)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../FlowBlox.Build/data/skill_project/{name}.fbprj"));
        var project = FlowBloxProject.FromFile(path);
        FlowBloxProjectManager.Instance.ActiveProject = project;
        return project;
    }
}
