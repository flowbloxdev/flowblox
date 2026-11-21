using FlowBlox.Core.Models.Components.IO;
using OfficeOpenXml;

namespace FlowBloxTest.FlowBlocks.IO;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ExcelTableReadTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "flowblox-excel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        ExcelPackage.License.SetNonCommercialPersonal("FlowBlox unit tests");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    [TestMethod]
    public void SparseHeadersPreserveLaterColumnValuesAndPositions()
    {
        var path = Path.Combine(_directory, "sparse.xlsx");
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Data");
            sheet.Cells[1, 1].Value = "First";
            sheet.Cells[1, 3].Value = "Third";
            sheet.Cells[2, 1].Value = "left";
            sheet.Cells[2, 3].Value = "middle";
            sheet.Cells[2, 6].Value = "right";
            package.SaveAs(new FileInfo(path));
        }
        var table = new ExcelTable { DataSource = new FileObject { FilePath = path }, FirstRowHeader = true };
        var result = table.Read();
        Assert.AreEqual(6, result.Columns.Count);
        Assert.AreEqual("middle", result.Rows[0]["Third"]);
        Assert.AreEqual("right", result.Rows[0][5]);
        Assert.AreEqual(DBNull.Value, result.Rows[0][1]);
    }

    [TestMethod]
    public void DuplicateAndMissingHeadersRetainIndependentColumns()
    {
        var path = Path.Combine(_directory, "duplicates.xlsx");
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Data");
            sheet.Cells[1, 1].Value = "Name";
            sheet.Cells[1, 3].Value = "Name";
            sheet.Cells[1, 4].Value = "Column 2";
            for (var column = 1; column <= 4; column++)
                sheet.Cells[2, column].Value = "value-" + column;
            package.SaveAs(new FileInfo(path));
        }
        var table = new ExcelTable { DataSource = new FileObject { FilePath = path }, FirstRowHeader = true };
        var result = table.Read();
        var names = result.Columns.Cast<System.Data.DataColumn>()
            .Select(column => column.ColumnName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.HasCount(4, names);
        CollectionAssert.AreEqual(new object[] { "value-1", "value-2", "value-3", "value-4" }, result.Rows[0].ItemArray);
    }

    [TestMethod]
    public void SparseRowsWithoutHeadersKeepLeadingAndIntermediateEmptyCells()
    {
        var path = Path.Combine(_directory, "no-headers.xlsx");
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Data");
            sheet.Cells[1, 3].Value = "first row";
            sheet.Cells[2, 5].Value = "second row";
            package.SaveAs(new FileInfo(path));
        }
        var table = new ExcelTable { DataSource = new FileObject { FilePath = path }, FirstRowHeader = false };
        var result = table.Read();
        Assert.AreEqual(5, result.Columns.Count);
        Assert.AreEqual(2, result.Rows.Count);
        Assert.AreEqual("first row", result.Rows[0][2]);
        Assert.AreEqual("second row", result.Rows[1][4]);
    }
}
