using FlowBlox.Core.Models.Components.IO;
using OfficeOpenXml;
using System.Data;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;

namespace FlowBloxTest.FlowBlocks.IO
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class ExcelTableTests
    {
        [TestMethod]
        public void Read_EmptyWorksheet_ReturnsEmptyTable()
        {
            ExcelPackage.License.SetNonCommercialPersonal("FlowBlox Tests");
            var dataSource = new TestDataObject();
            var excelTable = new ExcelTable { DataSource = dataSource };

            excelTable.Write(new DataTable());

            var result = excelTable.Read();

            Assert.AreEqual(0, result.Columns.Count);
            Assert.AreEqual(0, result.Rows.Count);
        }

        [TestMethod]
        public void Read_UsesSelectedHeaderFlagOutsideInvocationAndKeepsConfiguredLiteral()
        {
            ExcelPackage.License.SetNonCommercialPersonal("FlowBlox Tests");
            var project = new FlowBloxProject();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            var flag = project.FlowBloxRegistry.CreateUserField(UserFieldTypes.Memory, FieldTypes.Boolean, "Header");
            flag.StringValue = "false";
            var table = new ExcelTable { DataSource = new TestDataObject(), FirstRowHeader = true };
            var content = new DataTable();
            content.Columns.Add("Header");
            content.Rows.Add("Value");
            table.Write(content);
            table.SetPropertySelectedField(nameof(table.FirstRowHeader), flag);

            var withoutHeader = table.Read();
            Assert.AreEqual("Column 1", withoutHeader.Columns[0].ColumnName);
            Assert.AreEqual(2, withoutHeader.Rows.Count);
            Assert.AreEqual("Header", withoutHeader.Rows[0][0]);

            flag.StringValue = "true";
            var withHeader = table.Read();
            Assert.AreEqual("Header", withHeader.Columns[0].ColumnName);
            Assert.AreEqual(1, withHeader.Rows.Count);
            Assert.AreEqual("Value", withHeader.Rows[0][0]);
            Assert.IsTrue(table.FirstRowHeader);
        }

        [TestMethod]
        public void NamedWorksheetWritePreservesOtherSheetsAndIndexRemainsZeroBased()
        {
            ExcelPackage.License.SetNonCommercialPersonal("FlowBlox Tests");
            var source = new TestDataObject();
            var first = new ExcelTable { DataSource = source, WorksheetName = "Files" };
            var second = new ExcelTable { DataSource = source, WorksheetName = "Directories" };
            var data = new DataTable();
            data.Columns.Add("Path");
            data.Rows.Add("first.txt");
            first.Write(data);
            data.Rows[0][0] = "directory";
            second.Write(data);

            Assert.AreEqual("first.txt", first.Read().Rows[0][0]);
            Assert.AreEqual("directory", second.Read().Rows[0][0]);
            Assert.AreEqual("first.txt", new ExcelTable { DataSource = source }.Read().Rows[0][0]);
            Assert.AreEqual("directory", new ExcelTable { DataSource = source, WorksheetIndex = 1 }.Read().Rows[0][0]);
            first.WorksheetIndex = 99;
            Assert.AreEqual("first.txt", first.Read().Rows[0][0]);
        }

        [TestMethod]
        public void ReadingAnUnknownWorksheetDoesNotSilentlyReadTheFirstSheet()
        {
            ExcelPackage.License.SetNonCommercialPersonal("FlowBlox Tests");
            var source = new TestDataObject();
            var table = new ExcelTable { DataSource = source };
            table.Write(new DataTable());
            table.WorksheetName = "Missing";
            Assert.ThrowsExactly<InvalidOperationException>(() => table.Read());
        }

        private sealed class TestDataObject : DataObjectBase
        {
            public override byte[] Content { get; set; } = Array.Empty<byte>();

            public override bool CanRead() => Content.Length > 0;
        }
    }
}
