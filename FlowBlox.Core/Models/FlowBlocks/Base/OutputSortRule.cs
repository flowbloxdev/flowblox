using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;

namespace FlowBlox.Core.Models.FlowBlocks.Base;

/// <summary>
/// An automatically maintained output-field association with an optional sort direction.
/// </summary>
public sealed class OutputSortRule : FlowBloxReactiveObject
{
    [Required]
    [Display(Name = "Global_FieldElement", ResourceType = typeof(FlowBloxTexts), Order = 0)]
    [FlowBloxUI(ReadOnly = true)]
    [FlowBloxComboBox]
    public FieldElement Field { get; set; }

    private OutputSortDirection _direction;

    [Display(Name = "OutputSortRule_Direction", ResourceType = typeof(FlowBloxTexts), Order = 1)]
    [FlowBloxUI(UISelectionButtons.None)]
    public OutputSortDirection Direction
    {
        get => _direction;
        set
        {
            _direction = value;
            OnPropertyChanged();
        }
    }
}
