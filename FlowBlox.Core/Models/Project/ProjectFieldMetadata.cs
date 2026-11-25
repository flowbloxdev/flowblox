using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Project;

public sealed class ProjectFieldMetadata
{
    [Display(Name = "PropertyNames_Name", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
    public string Name { get; init; }

    [Display(Name = "FieldElement_FieldType", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
    public FieldTypes FieldType { get; init; }

    [Display(Name = "FieldElement_DefaultValue", Description = "FieldElement_DefaultValue_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
    [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
    public string DefaultValue { get; init; }

    [Display(Name = "FieldElement_UsePasswordChar", ResourceType = typeof(FlowBloxTexts), Order = 3)]
    [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
    public bool IsPassword { get; init; }

    [Display(Name = "FieldElement_IsRequiredAtRuntimeStartup", ResourceType = typeof(FlowBloxTexts), Order = 4)]
    [FlowBloxUI(UISelectionButtons.None, ReadOnly = true)]
    public bool IsRequiredAtRuntimeStartup { get; init; }

    public LoVType LoVType { get; init; }
    public bool ClearOnRuntimeStart { get; init; }
    public string SyntaxHighlighting { get; init; }
    public FieldDisplayMode DisplayMode { get; init; }
    public string ListSeparator { get; init; } = @"\n";
    public IReadOnlyList<string> ListOfValues { get; init; } = [];
}
