using System;
using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Linq;
using FlowBlox.Core;
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
    // Only closed, standalone info containers are interpreted. Other text remains literal.
    private static readonly Regex InfoBoxPattern = new(
        @"^:::box info(?: (?<icon>bulb|alert))?[^\S\r\n]*\r?\n(?<text>[\s\S]*?)\r?\n:::[^\S\r\n]*(?=\r?$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Geometry BulbGeometry = LoadIconGeometry(FlowBloxIcons.lightbulb_outline);
    private static readonly Geometry AlertGeometry = LoadIconGeometry(FlowBloxIcons.alert_circle_outline);
    private static readonly IBrush InfoForeground = Brush.Parse("#D7D7DA");
    private static readonly IBrush BulbBrush = Brush.Parse("#E6C86E");
    private static readonly IBrush AlertBrush = Brush.Parse("#E6BD65");
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
        var displayText = SetMessage(message ?? string.Empty);
        // Include the icon column, horizontal margins and border in the window width.
        var measured = new FormattedText(displayText, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily), 13, Brushes.White);
        var measuredTitle = new FormattedText(title ?? string.Empty, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface(FontFamily), 23, Brushes.White);
        const double horizontalChrome = 64 + 48 + 2;
        Width = Math.Clamp(
            Math.Max(measured.WidthIncludingTrailingWhitespace, measuredTitle.WidthIncludingTrailingWhitespace) + horizontalChrome
                + (InfoBoxPattern.IsMatch(message ?? string.Empty) ? 24 : 0),
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

    private string SetMessage(string text)
    {
        var blocks = this.FindControl<StackPanel>("MessageBlocks");
        var position = 0;
        foreach (Match match in InfoBoxPattern.Matches(text))
        {
            AddPlainText(blocks, text[position..match.Index].TrimEnd('\r', '\n'));
            blocks.Children.Add(CreateInfoHint(match.Groups["text"].Value, match.Groups["icon"].Value));
            position = match.Index + match.Length;
        }
        AddPlainText(blocks, position == 0 ? text : text[position..].TrimStart('\r', '\n'));
        return InfoBoxPattern.Replace(text, "${text}");
    }

    private static void AddPlainText(StackPanel blocks, string text)
    {
        if (text.Length > 0)
            blocks.Children.Add(CreateMessageText(text, false));
    }

    private static Geometry LoadIconGeometry(byte[] svg)
    {
        var document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(svg));
        var path = document.Descendants().Single(element => element.Name.LocalName == "path");
        return Geometry.Parse(path.Attribute("d").Value);
    }

    private static global::Avalonia.Controls.Grid CreateInfoHint(string text, string iconName)
    {
        var grid = new global::Avalonia.Controls.Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,*")
        };
        Control icon;
        if (iconName == "bulb" || iconName == "alert")
        {
            icon = new global::Avalonia.Controls.Shapes.Path
            {
                Data = iconName == "alert" ? AlertGeometry : BulbGeometry,
                Fill = iconName == "alert" ? AlertBrush : BulbBrush,
                Stretch = Stretch.Uniform,
                Width = 18,
                Height = 18,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top
            };
        }
        else
        {
            icon = new TextBlock
            {
                Text = "i",
                Foreground = InfoForeground,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center
            };
        }
        grid.Children.Add(icon);
        var content = CreateMessageText(text, true);
        content.Margin = new Thickness(6, 0, 0, 0);
        global::Avalonia.Controls.Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private static SelectableTextBlock CreateMessageText(string text, bool info)
    {
        var message = new SelectableTextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = info ? InfoForeground : Brushes.White,
            Inlines = new InlineCollection()
        };
        var linkBrush = LinkBrush;
        var hoverLinkBrush = HoverLinkBrush;
        var position = 0;
        foreach (var link in WebLinks.Find(text))
        {
            message.Inlines.Add(new Run(text[position..link.Start]));
            var label = new TextBlock
            {
                Text = text.Substring(link.Start, link.Length),
                FontFamily = message.FontFamily,
                FontSize = message.FontSize,
                Foreground = linkBrush,
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
            button.PointerEntered += (_, _) => label.Foreground = hoverLinkBrush;
            button.PointerExited += (_, _) => label.Foreground = linkBrush;
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
        return message;
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
