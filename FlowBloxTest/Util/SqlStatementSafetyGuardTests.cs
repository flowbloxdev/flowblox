using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;

namespace FlowBloxTest.Util
{
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class SqlStatementSafetyGuardTests
    {
        private string _originalValue = null!;

        [TestInitialize]
        public void Initialize()
        {
            var option = FlowBloxOptions.GetOptionInstance()
                .GetOption(OptionsConstants.SQLAllowDestructiveStatements)!;
            _originalValue = option.Value;
            option.Value = "false";
        }

        [TestCleanup]
        public void Cleanup()
        {
            FlowBloxOptions.GetOptionInstance()
                .GetOption(OptionsConstants.SQLAllowDestructiveStatements)!
                .Value = _originalValue;
        }

        [TestMethod]
        [DataRow("DELETE FROM Customers")]
        [DataRow("DROP TABLE Customers")]
        [DataRow("ALTER TABLE Customers DROP COLUMN Legacy")]
        [DataRow("TRUNCATE TABLE Customers")]
        public void EnsureAllowed_RejectsDestructiveStatementsWhenDisabled(string sql)
        {
            var exception = Assert.ThrowsExactly<NotSupportedException>(() =>
                SqlStatementSafetyGuard.EnsureAllowed(sql));

            StringAssert.Contains(exception.Message, OptionsConstants.SQLAllowDestructiveStatements);
        }

        [TestMethod]
        public void EnsureAllowed_AllowsReadQueriesAndQuotedKeywords()
        {
            SqlStatementSafetyGuard.EnsureAllowed(
                "SELECT 'DELETE FROM Customers' AS Example /* DROP TABLE Ignored */");
        }

        [TestMethod]
        public void UnquotedTokensReader_SkipsCommentsAndQuotedContent()
        {
            var tokens = SqlUnquotedTokensReader.Read(
                    "SELECT 'DELETE' AS [DROP], \"TRUNCATE\" FROM `DELETE` -- DROP TABLE Ignored\r\nWHERE Id = 1")
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { "SELECT", "AS", "FROM", "WHERE", "Id" },
                tokens);
        }

        [TestMethod]
        public void EnsureAllowed_AllowsDestructiveStatementsWhenEnabled()
        {
            FlowBloxOptions.GetOptionInstance()
                .GetOption(OptionsConstants.SQLAllowDestructiveStatements)!
                .Value = "true";

            SqlStatementSafetyGuard.EnsureAllowed("DELETE FROM Customers");
        }
    }
}
