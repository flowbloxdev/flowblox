using System;
using System.Globalization;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;

namespace FlowBlox.AvaloniaDesktop.Dialogs;

internal sealed partial class MessageBoxWindow : Window
{
    private static readonly IBrush LinkBrush = Brush.Parse("#82C5FF");
    private static readonly IBrush HoverLinkBrush = Brush.Parse("#B7DFFF");

    public MessageBoxWindow(
        string message,
        string title,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        string yesButtonText = null,
        string noButtonText = null,
        MessageBoxResult defaultResult = MessageBoxResult.No)
    {
        AvaloniaXamlLoader.Load(this);
        Title = title;
        this.FindControl<TextBlock>("Caption").Text = title;
        SetMessage(message ?? string.Empty);
        // Include the icon column, horizontal margins and border in the window width.
        var measured = new FormattedText(message ?? string.Empty, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily), 13, Brushes.White);
        var measuredTitle = new FormattedText(title ?? string.Empty, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily), 23, Brushes.White);
        const double horizontalChrome = 64 + 48 + 2;
        Width = Math.Clamp(
            Math.Max(measured.WidthIncludingTrailingWhitespace, measuredTitle.WidthIncludingTrailingWhitespace) + horizontalChrome,
            MinWidth,
            MaxWidth);
        var german = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        var affirmative = this.FindControl<Button>("Affirmative");
        var negative = this.FindControl<Button>("Negative");
        affirmative.Content = buttons == MessageBoxButtons.YesNo ? yesButtonText ?? (german ? "Ja" : "Yes") : "OK";
        negative.Content = noButtonText ?? (german ? "Nein" : "No");
        negative.IsVisible = buttons == MessageBoxButtons.YesNo;
        affirmative.IsDefault = buttons == MessageBoxButtons.Ok || defaultResult == MessageBoxResult.Yes;
        negative.IsDefault = buttons == MessageBoxButtons.YesNo && defaultResult == MessageBoxResult.No;
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
        Opened += (_, _) => (negative.IsDefault ? negative : affirmative).Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close(buttons == MessageBoxButtons.YesNo ? defaultResult : MessageBoxResult.Ok);
        };
    }

    private void SetMessage(string text)
    {
        var message = this.FindControl<SelectableTextBlock>("Message");
        message.Inlines = new InlineCollection();
        var position = 0;
        foreach (var link in WebLinks.Find(text))
        {
            message.Inlines.Add(new Run(text[position..link.Start]));
            var label = new TextBlock
            {
                Text = text.Substring(link.Start, link.Length),
                FontFamily = message.FontFamily,
                FontSize = message.FontSize,
                Foreground = LinkBrush,
                TextDecorations = TextDecorations.Underline,
                TextWrapping = TextWrapping.Wrap
            };
            var button = new HyperlinkButton
            {
                Content = label,
                FontFamily = message.FontFamily,
                FontSize = message.FontSize,
                MinHeight = 0,
                MinWidth = 0,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(button, link.Uri.AbsoluteUri);
            button.PointerEntered += (_, _) => label.Foreground = HoverLinkBrush;
            button.PointerExited += (_, _) => label.Foreground = LinkBrush;
            button.Click += (_, _) => OpenLink(link.Uri);
            // Embedded controls otherwise align their bottom edge with the text baseline.
            label.Measure(Size.Infinity);
            TextBlock.SetBaselineOffset(button, label.TextLayout.TextLines[0].Baseline);
            message.Inlines.Add(new InlineUIContainer
            {
                Child = button,
                BaselineAlignment = BaselineAlignment.Baseline
            });
            position = link.Start + link.Length;
        }
        message.Inlines.Add(new Run(text[position..]));
    }

    private static void OpenLink(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to open a message-box web link.", exception);
        }
    }
}
