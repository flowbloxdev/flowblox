using System.Data.Common;

namespace FlowBlox.Core.Util.FullTextSearch;

internal sealed class FullTextTable
{
    public string[] NameParts { get; set; } = [];
    public string Alias { get; set; } = string.Empty;
    public List<DbColumn> Columns { get; set; } = [];
    public List<string> TextColumns { get; set; } = [];
    public string KeyColumn { get; set; } = string.Empty;
    public string KeyIndex { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
}
