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
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                        return dataTable;

                    var dimension = worksheet.Dimension;
                    if (dimension == null)
                        return dataTable;

                    var firstRowHeader = this.Resolve(table => table.FirstRowHeader);
                    foreach (var firstRowCell in worksheet.Cells[1, 1, 1, dimension.End.Column])
                    {
                        dataTable.Columns.Add(firstRowHeader ? firstRowCell.Text : $"Column {firstRowCell.Start.Column}");
                    }

                    var startRow = firstRowHeader ? 2 : 1;
                    for (int rowNum = startRow; rowNum <= dimension.End.Row; rowNum++)
                    {
                        var wsRow = worksheet.Cells[rowNum, 1, rowNum, dimension.End.Column];
                        DataRow row = dataTable.Rows.Add();
                        foreach (var cell in wsRow)
                        {
                            row[cell.Start.Column - 1] = cell.Text;
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

            using (var memoryStream = new MemoryStream())
            {
                using (var package = new ExcelPackage(memoryStream))
                {
                    ExcelWorksheet worksheet = package.Workbook.Worksheets.Add("Sheet1");
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

                    package.Save();
                }

                DataSource.Content = memoryStream.ToArray();
            }
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




