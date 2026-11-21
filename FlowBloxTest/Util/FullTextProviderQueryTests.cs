using FlowBlox.Core.Enums;
using FlowBlox.Core.Util.FullTextSearch;

namespace FlowBloxTest.Util;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class FullTextProviderQueryTests
{
    [TestMethod]
    [DataRow(DbTypes.SQLite, "bm25(", "LIMIT 5")]
    [DataRow(DbTypes.MySQL, "AGAINST (", "LIMIT 5")]
    [DataRow(DbTypes.MSSQL, "FREETEXTTABLE(", "OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY")]
    [DataRow(DbTypes.Oracle, "SCORE(", "FETCH FIRST 5 ROWS ONLY")]
    public void ProviderCompilation_BindsSearchTextAndPreservesPagination(DbTypes provider, string ranking, string pagination)
    {
        var marker = provider == DbTypes.Oracle ? ":query" : "@query";
        var query = new FullTextSql($"SELECT Id,FULLTEXT_RANK() AS Score FROM documents d WHERE FULLTEXT_MATCH(Title,Content,{marker}) {pagination}");
        var table = new FullTextTable
        {
            NameParts = ["documents"], Alias = "d", TextColumns = ["Title", "Content"],
            KeyColumn = "Id", KeyIndex = "PK_documents", IndexName = "fbx_example"
        };
        const string input = "quartz \" ); DROP TABLE documents; -- cobalt";
        var parameters = new Dictionary<string, object> { [marker] = input };

        var sql = FullTextSearchBase.For(provider).PrepareQuery(query, table, parameters);

        StringAssert.Contains(sql, ranking);
        StringAssert.Contains(sql, pagination);
        StringAssert.Contains(sql, provider == DbTypes.Oracle ? ":fbx_search" : "@fbx_search");
        Assert.IsFalse(sql.Contains(input, StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("FULLTEXT_MATCH", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("FULLTEXT_RANK", StringComparison.Ordinal));
        Assert.IsTrue(sql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase) < sql.IndexOf(pagination, StringComparison.Ordinal));
        Assert.AreEqual(2, parameters.Count);
    }

    [TestMethod]
    public void LiteralAndParameterNames_CannotChangeSelectedTable()
    {
        var query = new FullTextSql("SELECT 'FROM other' AS Label,Title FROM [main].[documents] AS d WHERE FULLTEXT_MATCH(Title,@q)");
        CollectionAssert.AreEqual(new[] { "main", "documents" }, query.TableParts);
        Assert.AreEqual("d", query.Alias);
        CollectionAssert.AreEqual(new[] { "Title" }, query.SearchColumns);
    }
}
