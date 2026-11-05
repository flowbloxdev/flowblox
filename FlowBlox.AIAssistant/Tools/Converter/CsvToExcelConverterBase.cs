using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;
using System.Text;

namespace FlowBlox.AIAssistant.Tools.Converter
{
    internal abstract class CsvToExcelConverterBase
    {
        protected static List<List<string>> ParseCsvRows(string input)
        {
            var rows = new List<List<string>>();
            var currentRow = new List<string>();
            var currentCell = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < input.Length; i++)
            {
                var ch = input[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        var nextIsQuote = i + 1 < input.Length && input[i + 1] == '"';
                        if (nextIsQuote)
                        {
                            currentCell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentCell.Append(ch);
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (ch == ',')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    continue;
                }

                if (ch == '\r')
                {
                    continue;
                }

                if (ch == '\n')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    continue;
                }

                currentCell.Append(ch);
            }

            if (currentCell.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentCell.ToString());
                rows.Add(currentRow);
            }

            return rows;
        }

        protected static void ConfigureEpplusLicense()
        {
            var options = FlowBloxOptions.GetOptionInstance().OptionCollection;
            var licenseType = options[OptionsConstants.EPPlusLicenseType]?.Value?.Trim()?.ToLowerInvariant();
            var licenseKeyOrName = options[OptionsConstants.EPPlusLicenseKeyOrName]?.Value;
            if (string.IsNullOrWhiteSpace(licenseKeyOrName))
                licenseKeyOrName = Environment.UserName;

            switch (licenseType)
            {
                case "noncommercialorganization":
                    OfficeOpenXml.ExcelPackage.License.SetNonCommercialOrganization(licenseKeyOrName);
                    break;
                case "commercial":
                    OfficeOpenXml.ExcelPackage.License.SetCommercial(licenseKeyOrName);
                    break;
                default:
                    OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal(licenseKeyOrName);
                    break;
            }
        }
    }
}
