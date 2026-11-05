using FlowBlox.Core.Constants;
using FlowBlox.Core.Util;
using OfficeOpenXml;
using System.Text;

namespace FlowBlox.AIAssistant.Tools.Converter
{
    internal static class Xlsx2CsvConverter
    {
        public const string Name = "Xlsx2CsvConverter";

        public static string Convert(byte[] xlsxBytes)
        {
            ConfigureEpplusLicense();

            using var memoryStream = new MemoryStream(xlsxBytes ?? Array.Empty<byte>());
            using var package = new ExcelPackage(memoryStream);
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet?.Dimension == null)
                return string.Empty;

            var sb = new StringBuilder();
            for (var row = worksheet.Dimension.Start.Row; row <= worksheet.Dimension.End.Row; row++)
            {
                var values = new List<string>();
                for (var col = worksheet.Dimension.Start.Column; col <= worksheet.Dimension.End.Column; col++)
                {
                    var value = worksheet.Cells[row, col].Text ?? string.Empty;
                    values.Add(EscapeCsv(value));
                }

                sb.AppendLine(string.Join(",", values));
            }

            return sb.ToString();
        }

        private static string EscapeCsv(string value)
        {
            var v = value ?? string.Empty;
            if (!v.Contains(',') && !v.Contains('"') && !v.Contains('\n') && !v.Contains('\r'))
                return v;

            return $"\"{v.Replace("\"", "\"\"")}\"";
        }

        private static void ConfigureEpplusLicense()
        {
            var options = FlowBloxOptions.GetOptionInstance().OptionCollection;
            var licenseType = options[OptionsConstants.EPPlusLicenseType]?.Value?.Trim()?.ToLowerInvariant();
            var licenseKeyOrName = options[OptionsConstants.EPPlusLicenseKeyOrName]?.Value;
            if (string.IsNullOrWhiteSpace(licenseKeyOrName))
                licenseKeyOrName = Environment.UserName;

            switch (licenseType)
            {
                case "noncommercialorganization":
                    ExcelPackage.License.SetNonCommercialOrganization(licenseKeyOrName);
                    break;
                case "commercial":
                    ExcelPackage.License.SetCommercial(licenseKeyOrName);
                    break;
                default:
                    ExcelPackage.License.SetNonCommercialPersonal(licenseKeyOrName);
                    break;
            }
        }
    }
}
