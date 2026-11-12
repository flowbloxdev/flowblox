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
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public bool Enabled { get; set; }

    [Display(Name = nameof(LiteralOnly))]
    public bool LiteralOnly { get; set; }

    [Display(Name = nameof(Count))]
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public int Count { get; set; } = 7;

    [Display(Name = nameof(Since))]
    [ActivationCondition(MemberName = nameof(Enabled), Value = true)]
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public DateTime? Since { get; set; }

    [Display(Name = nameof(Encoding))]
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public DotNetEncodingNames Encoding { get; set; }

    [Display(Name = nameof(Text))]
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public string Text { get; set; } = "Literal";

    [Display(Name = nameof(Password))]
    [FlowBloxTextBox(IsPassword = true)]
    [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = nameof(Choice))]
    [FlowBloxUI(Factory = UIFactory.ComboBox, SelectionFilterMethod = nameof(GetChoices), UiOptions = UIOptions.EnableFieldSelection)]
    [FlowBloxComboBox(DropDownMode = DropDownMode.SelectionOnly)]
    public string Choice { get; set; } = "One";

    public string[] GetChoices() => new[] { "One", "Two" };

    [Display(Name = nameof(Rows))]
    [FlowBloxUI(Factory = UIFactory.GridView)]
    [FlowBloxDataGrid(GridColumnMemberNames = new[] { nameof(Enabled), nameof(Count), nameof(Since), nameof(Encoding), nameof(Text) })]
    public ObservableCollection<SimplePropertyFieldSelectionSettings> Rows { get; set; } = new();
}
