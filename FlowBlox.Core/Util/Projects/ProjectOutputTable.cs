using System.Data;
using System.Globalization;
using System.Text.Json;
using FlowBlox.Core.Runner.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.Core.Util.Projects;

/// <summary>
/// Presents runner outputs consistently in desktop and web views without changing the response values.
/// </summary>
public static class ProjectOutputTable
{
    public static DataTable Create(IEnumerable<ProjectOutputDatasetDto> datasets)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        foreach (var dataset in datasets)
        {
            foreach (var name in dataset.Values.Keys)
            {
                if (!table.Columns.Contains(name))
                    table.Columns.Add(name, typeof(string));
            }

            var row = table.NewRow();
            foreach (var value in dataset.Values)
                row[value.Key] = FormatValue(value.Value);
            table.Rows.Add(row);
        }
        return table;
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
            return string.Empty;
        if (value is string text)
            return text;
        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String)
                return json.GetString() ?? string.Empty;
            if (json.ValueKind == JsonValueKind.Null || json.ValueKind == JsonValueKind.Undefined)
                return string.Empty;
            return json.ToString();
        }
        if (value is JValue scalar)
            return Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (value is byte[] bytes)
            return Convert.ToBase64String(bytes);
        if (value is IFormattable formatted)
            return formatted.ToString(null, CultureInfo.InvariantCulture);
        return JsonConvert.SerializeObject(value, Formatting.Indented);
    }
}
