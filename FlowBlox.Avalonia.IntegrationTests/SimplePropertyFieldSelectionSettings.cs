using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Enums;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed class SimplePropertyFieldSelectionSettings : FlowBloxReactiveObject
{
    [Display(Name = nameof(Enabled))]
    public bool Enabled { get; set; }

    [Display(Name = nameof(LiteralOnly))]
    [FlowBloxUI(UISelectionButtons.None)]
    public bool LiteralOnly { get; set; }

    [Display(Name = nameof(Count))]
    public int Count { get; set; } = 7;

    [Display(Name = nameof(Since))]
    [ActivationCondition(MemberName = nameof(Enabled), Value = true)]
    public DateTime? Since { get; set; }

    [Display(Name = nameof(Encoding))]
    public DotNetEncodingNames Encoding { get; set; }

    [Display(Name = nameof(Text))]
    public string Text { get; set; } = "Literal";

    [Display(Name = nameof(Password))]
    [FlowBloxTextBox(IsPassword = true)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = nameof(Choice))]
    [FlowBloxComboBox(DropDownMode = DropDownMode.SelectionOnly)]
    [FlowBloxItemsSource(Method = nameof(GetChoices))]
    public string Choice { get; set; } = "One";

    public string[] GetChoices() => new[] { "One", "Two" };

    [Display(Name = nameof(Rows))]
    [FlowBloxDataGrid(ColumnMemberNames = new[] { nameof(Enabled), nameof(Count), nameof(Since), nameof(Encoding), nameof(Text) })]
    public ObservableCollection<SimplePropertyFieldSelectionSettings> Rows { get; set; } = new();
}
