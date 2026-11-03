using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FlowBlox.AvaloniaDesktop.Services;

namespace FlowBlox.AvaloniaDesktop.Dialogs;

internal sealed partial class MessageBoxWindow : Window
{
    public MessageBoxWindow(string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        AvaloniaXamlLoader.Load(this);
        Title = title;
        this.FindControl<TextBlock>("Caption").Text = title;
        this.FindControl<SelectableTextBlock>("Message").Text = message;
        // Preserve the legacy sizing rule: 350 for short messages, growing with
        // their measured text width; messages of 150+ characters use 700.
        var measured = new FormattedText(message ?? string.Empty, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily), 13, Brushes.White);
        Width = message?.Length >= 150 ? MaxWidth : Math.Clamp(measured.WidthIncludingTrailingWhitespace, MinWidth, MaxWidth);
        var german = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        var affirmative = this.FindControl<Button>("Affirmative");
        var negative = this.FindControl<Button>("Negative");
        affirmative.Content = buttons == MessageBoxButtons.YesNo ? german ? "Ja" : "Yes" : "OK";
        negative.Content = german ? "Nein" : "No";
        negative.IsVisible = buttons == MessageBoxButtons.YesNo;
        affirmative.IsDefault = buttons == MessageBoxButtons.Ok;
        negative.IsDefault = buttons == MessageBoxButtons.YesNo;
        affirmative.Click += (_, _) => Close(buttons == MessageBoxButtons.YesNo ? MessageBoxResult.Yes : MessageBoxResult.Ok);
        negative.Click += (_, _) => Close(MessageBoxResult.No);
        var (symbol, color) = icon switch
        {
            MessageBoxIcon.Error => ("!", "#D44D5C"),
            MessageBoxIcon.Warning => ("!", "#BA8B2B"),
            MessageBoxIcon.Question => ("?", "#4775C8"),
            _ => ("i", "#4775C8")
        };
        this.FindControl<TextBlock>("Symbol").Text = symbol;
        this.FindControl<Border>("SymbolBackground").Background = Brush.Parse(color);
        Opened += (_, _) => (buttons == MessageBoxButtons.YesNo ? negative : affirmative).Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close(buttons == MessageBoxButtons.YesNo ? MessageBoxResult.No : MessageBoxResult.Ok);
        };
    }
}
