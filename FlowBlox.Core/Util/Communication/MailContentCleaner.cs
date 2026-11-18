using MimeKit;
using MimeKit.Text;

namespace FlowBlox.Core.Util.Communication;

/// <summary>
/// Removes recognizable reply quotations while keeping inline replies and the original MIME message unchanged.
/// Uses structural HTML quotation markers and plain-text quote prefixes without localized reply-header heuristics.
/// </summary>
internal static class MailContentCleaner
{
    public static string GetOriginalContent(MimeMessage message) =>
        message.TextBody ??
        message.HtmlBody ??
        message.Body?.ToString() ?? string.Empty;

    public static string GetCurrentContent(MimeMessage message)
    {
        var text = message.TextBody;
        if (message.HtmlBody != null)
        {
            var cleanedHtml = RemoveHtmlQuotes(message.HtmlBody, out var foundQuotation);
            if (foundQuotation || text == null)
                text = cleanedHtml;
        }

        return RemoveTextQuotes(text ?? string.Empty);
    }

    private static string RemoveTextQuotes(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var kept = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('>'))
                continue;

            kept.Add(lines[index]);
        }

        return string.Join(Environment.NewLine, kept).Trim();
    }

    private static string RemoveHtmlQuotes(string html, out bool foundQuotation)
    {
        foundQuotation = false;
        using var input = new StringReader(html);
        using var output = new StringWriter();
        var tokenizer = new HtmlTokenizer(input) { DecodeCharacterReferences = true };
        string? skippedTag = null;
        var depth = 0;

        while (tokenizer.ReadNextToken(out var token))
        {
            if (token is HtmlTagToken tag)
            {
                if (skippedTag != null)
                {
                    if (tag.Name.Equals(skippedTag, StringComparison.OrdinalIgnoreCase))
                    {
                        if (tag.IsEndTag)
                            depth--;
                        else if (!tag.IsEmptyElement)
                            depth++;
                    }
                    if (depth == 0)
                        skippedTag = null;
                    continue;
                }

                if (!tag.IsEndTag)
                {
                    var id = tag.Attributes.FirstOrDefault(attribute => attribute.Name.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value;
                    if (string.Equals(id, "divRplyFwdMsg", StringComparison.OrdinalIgnoreCase))
                    {
                        foundQuotation = true;
                        break;
                    }

                    var classes = tag.Attributes.FirstOrDefault(attribute => attribute.Name.Equals("class", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
                    var isQuote = classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Any(value => value is "gmail_quote" or "yahoo_quoted" or "moz-cite-prefix");
                    if (tag.Id == HtmlTagId.BlockQuote || tag.Id == HtmlTagId.Script || tag.Id == HtmlTagId.Style || isQuote)
                    {
                        if (tag.Id == HtmlTagId.BlockQuote || isQuote)
                            foundQuotation = true;

                        if (!tag.IsEmptyElement)
                        {
                            skippedTag = tag.Name;
                            depth = 1;
                        }
                        continue;
                    }
                }

                if (tag.Name.ToLowerInvariant() is "br" or "p" or "div" or "li" or "tr" or "table" or "hr" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                    output.WriteLine();
            }

            if (skippedTag == null && token is HtmlDataToken data)
                output.Write(data.Data);
        }

        return output.ToString();
    }
}
