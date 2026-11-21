using FlowBlox.Core.Extensions;
using FlowBlox.Core.Constants;
using SkiaSharp;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using OfficeOpenXml;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace FlowBlox.Core.Models.Components.IO
{
    [Display(Name = "ExcelTable_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("ExcelTable_DisplayName_Plural", typeof(FlowBloxTexts))]
    public class ExcelTable : DataTableBase
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.microsoft_excel, 16, new SKColor(22, 163, 74));

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.microsoft_excel, 32, new SKColor(22, 163, 74));

        [Display(Name = "ExcelTable_WorksheetName", Description = "ExcelTable_WorksheetName_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        public string WorksheetName { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "ExcelTable_WorksheetIndex", Description = "ExcelTable_WorksheetIndex_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public int WorksheetIndex { get; set; }
        public ExcelTable() : base() 
        {

        }

        public override DataTable Read()
        {
            if (DataSource == null || !DataSource.CanRead())
            {
                throw new InvalidOperationException("The data source is not ready for reading.");
            }

            using (var memoryStream = new MemoryStream(DataSource.Content))
            {
                var dataTable = new DataTable();
                using (var package = new ExcelPackage(memoryStream))
                {
                    var worksheet = SelectWorksheet(package, create: false);
                    if (worksheet == null)
                        return dataTable;

                    var dimension = worksheet.Dimension;
                    if (dimension == null)
                        return dataTable;

                    var firstRowHeader = this.Resolve(table => table.FirstRowHeader);
                    for (var column = 1; column <= dimension.End.Column; column++)
                    {
                        var name = firstRowHeader ? worksheet.Cells[1, column].Text : string.Empty;
                        if (string.IsNullOrWhiteSpace(name))
                            name = $"Column {column}";

                        var uniqueName = name;
                        var suffix = 2;
                        while (dataTable.Columns.Contains(uniqueName))
                            uniqueName = $"{name}_{suffix++}";

                        dataTable.Columns.Add(uniqueName);
                    }

                    var startRow = firstRowHeader ? 2 : 1;
                    for (int rowNum = startRow; rowNum <= dimension.End.Row; rowNum++)
                    {
                        DataRow row = dataTable.Rows.Add();
                        for (var column = 1; column <= dimension.End.Column; column++)
                        {
                            var value = worksheet.Cells[rowNum, column].Text;
                            if (!string.IsNullOrEmpty(value))
                                row[column - 1] = value;
                        }
                    }
                }
                return dataTable;
            }
        }

        public override void Write(DataTable dataTable)
        {
            if (DataSource == null)
            {
                throw new InvalidOperationException("The data source is not ready for writing.");
            }

            var worksheetName = this.Resolve(table => table.WorksheetName);
            var worksheetIndex = this.Resolve(table => table.WorksheetIndex);
            var hasSelection = !string.IsNullOrWhiteSpace(worksheetName) || worksheetIndex != 0;
            var existingContent = hasSelection && DataSource.CanRead() ? DataSource.Content : null;
            using (var memoryStream = new MemoryStream())
            {
                using (var package = existingContent?.Length > 0
                    ? new ExcelPackage(new MemoryStream(existingContent))
                    : new ExcelPackage())
                {
                    var worksheet = SelectWorksheet(package, create: true);
                    worksheet.Cells.Clear();
                    for (int colIndex = 0; colIndex < dataTable.Columns.Count; colIndex++)
                    {
                        worksheet.Cells[1, colIndex + 1].Value = dataTable.Columns[colIndex].ColumnName;
                    }

                    for (int rowIndex = 0; rowIndex < dataTable.Rows.Count; rowIndex++)
                    {
                        for (int colIndex = 0; colIndex < dataTable.Columns.Count; colIndex++)
                        {
                            var value = dataTable.Rows[rowIndex][colIndex];
                            Type type = dataTable.Columns[colIndex].DataType;

                            if (type == typeof(string))
                            {
                                worksheet.Cells[rowIndex + 2, colIndex + 1].Value = value.ToString();
                                worksheet.Cells[rowIndex + 2, colIndex + 1].Style.Numberformat.Format = "@";
                            }
                            else
                            {
                                worksheet.Cells[rowIndex + 2, colIndex + 1].Value = value;
                            }
                        }
                    }

                    package.SaveAs(memoryStream);
                }

                DataSource.Content = memoryStream.ToArray();
            }
        }

        private ExcelWorksheet SelectWorksheet(ExcelPackage package, bool create)
        {
            var worksheetName = this.Resolve(table => table.WorksheetName);
            if (!string.IsNullOrWhiteSpace(worksheetName))
            {
                return package.Workbook.Worksheets[worksheetName]
                    ?? (create ? package.Workbook.Worksheets.Add(worksheetName)
                        : throw new InvalidOperationException($"Worksheet '{worksheetName}' was not found."));
            }

            var worksheetIndex = this.Resolve(table => table.WorksheetIndex);
            if (worksheetIndex < 0 || worksheetIndex >= package.Workbook.Worksheets.Count)
            {
                if (worksheetIndex == 0 && package.Workbook.Worksheets.Count == 0)
                    return create ? package.Workbook.Worksheets.Add("Sheet1") : null;

                throw new InvalidOperationException($"Worksheet index {worksheetIndex} is outside the workbook.");
            }

            return package.Workbook.Worksheets.ElementAt(worksheetIndex);
        }

        public override void OptionsInit(List<OptionElement> defaults)
        {
            defaults.Add(new OptionElement(
                "EPPlus.LicenseType",
                "NonCommercialPersonal",
                "Specify the license type: 'NonCommercialOrganization', 'NonCommercialPersonal', or 'Commercial'",
                OptionElement.OptionType.Text));

            defaults.Add(new OptionElement(
                "EPPlus.LicenseKeyOrName",
                Environment.UserName,
                "Optional license key or licensee name, depending on the license model.",
                OptionElement.OptionType.Text));

            base.OptionsInit(defaults);
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            var options = FlowBloxOptions.GetOptionInstance().OptionCollection;
            var licenseKeyOrName = options[OptionsConstants.EPPlusLicenseKeyOrName].Value;
            var licenseType = options[OptionsConstants.EPPlusLicenseType].Value?.Trim()?.ToLowerInvariant();
            switch (licenseType)
            {
                case "noncommercialorganization":
                    ExcelPackage.License.SetNonCommercialOrganization(licenseKeyOrName);
                    break;
                case "noncommercialpersonal":
                    ExcelPackage.License.SetNonCommercialPersonal(licenseKeyOrName);
                    break;

                case "commercial":
                    ExcelPackage.License.SetCommercial(licenseKeyOrName);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported EPPlus license type '{licenseType}'. Valid values are 'noncommercialorganization', 'noncommercialpersonal' or 'Commercial'.");
            }

            base.RuntimeStarted(runtime);
        }
    }
}




