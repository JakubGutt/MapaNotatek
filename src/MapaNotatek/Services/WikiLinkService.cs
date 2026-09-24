using System.Text.RegularExpressions;

namespace MapaNotatek.Services;

public static class WikiLinkService
{
    private static readonly Regex WikiLink = new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);

    public static IEnumerable<string> ExtractTitles(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            yield break;
        }

        foreach (Match match in WikiLink.Matches(body))
        {
            yield return match.Groups[1].Value.Trim();
        }
    }

    public static string ToHtmlPreview(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "<p style='opacity:0.6'>Brak treści</p>";
        }

        var escaped = System.Net.WebUtility.HtmlEncode(body);
        escaped = WikiLink.Replace(escaped, "<b>[[$1]]</b>");
        escaped = Regex.Replace(escaped, @"`([^`]+)`", "<code>$1</code>");
        escaped = Regex.Replace(escaped, @"\*\*([^*]+)\*\*", "<b>$1</b>");
        escaped = Regex.Replace(escaped, @"~~([^~]+)~~", "<del>$1</del>");
        escaped = Regex.Replace(escaped, @"\*([^*]+)\*", "<i>$1</i>");
        escaped = Regex.Replace(escaped, @"^### (.+)$", "<h3>$1</h3>", RegexOptions.Multiline);
        escaped = Regex.Replace(escaped, @"^## (.+)$", "<h2>$1</h2>", RegexOptions.Multiline);
        escaped = Regex.Replace(escaped, @"^# (.+)$", "<h1>$1</h1>", RegexOptions.Multiline);
        escaped = Regex.Replace(escaped, @"^- (.+)$", "• $1", RegexOptions.Multiline);
        escaped = escaped.Replace("\n", "<br/>");
        return $"<div style='font-family:sans-serif;font-size:13px;line-height:1.4'>{escaped}</div>";
    }

    /// <summary>Lightweight markdown → plain lines with markers for Avalonia TextBlock Inlines.</summary>
    public static IReadOnlyList<PreviewLine> ToPreviewLines(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [new PreviewLine(PreviewLineKind.Muted, "(brak treści)")];
        }

        var lines = new List<PreviewLine>();
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var checklist = Regex.Match(raw, @"^\s*[-*]\s+\[([ xX])\]\s+(.*)$");
            if (checklist.Success)
            {
                lines.Add(new PreviewLine(
                    checklist.Groups[1].Value.Equals("x", StringComparison.OrdinalIgnoreCase)
                        ? PreviewLineKind.ChecklistDone
                        : PreviewLineKind.ChecklistOpen,
                    checklist.Groups[2].Value));
            }
            else if (Regex.IsMatch(raw, @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$"))
            {
                lines.Add(new PreviewLine(PreviewLineKind.TableSeparator, raw));
            }
            else if (raw.Count(character => character == '|') >= 2)
            {
                lines.Add(new PreviewLine(PreviewLineKind.TableRow, raw));
            }
            else if (raw.Trim() is "---" or "***" or "___")
            {
                lines.Add(new PreviewLine(PreviewLineKind.Rule, string.Empty));
            }
            else if (raw.StartsWith("### ", StringComparison.Ordinal))
            {
                lines.Add(new PreviewLine(PreviewLineKind.H3, raw[4..]));
            }
            else if (raw.StartsWith("## ", StringComparison.Ordinal))
            {
                lines.Add(new PreviewLine(PreviewLineKind.H2, raw[3..]));
            }
            else if (raw.StartsWith("# ", StringComparison.Ordinal))
            {
                lines.Add(new PreviewLine(PreviewLineKind.H1, raw[2..]));
            }
            else if (raw.StartsWith("- ", StringComparison.Ordinal) || raw.StartsWith("* ", StringComparison.Ordinal))
            {
                lines.Add(new PreviewLine(PreviewLineKind.Bullet, raw[2..]));
            }
            else if (Regex.IsMatch(raw, @"^\d+\.\s"))
            {
                lines.Add(new PreviewLine(PreviewLineKind.Numbered, raw.TrimStart()));
            }
            else if (raw.StartsWith("> ", StringComparison.Ordinal))
            {
                lines.Add(new PreviewLine(PreviewLineKind.Quote, raw[2..]));
            }
            else if (string.IsNullOrWhiteSpace(raw))
            {
                lines.Add(new PreviewLine(PreviewLineKind.Blank, string.Empty));
            }
            else
            {
                lines.Add(new PreviewLine(PreviewLineKind.Paragraph, raw));
            }
        }

        return lines;
    }

    public static IEnumerable<InlinePreviewSpan> ParseInlineSpans(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var pattern = new Regex(
            @"(\[\[([^\]]+)\]\])|(`([^`]+)`)|(\*\*([^*]+)\*\*)|(~~([^~]+)~~)|(\*([^*]+)\*)",
            RegexOptions.Compiled);
        var index = 0;
        foreach (Match match in pattern.Matches(text))
        {
            if (match.Index > index)
            {
                yield return new InlinePreviewSpan(text[index..match.Index], false, false, false, false, false);
            }

            if (match.Groups[2].Success)
            {
                yield return new InlinePreviewSpan($"[[{match.Groups[2].Value}]]", true, false, false, false, true);
            }
            else if (match.Groups[4].Success)
            {
                yield return new InlinePreviewSpan(match.Groups[4].Value, false, false, true, false, false);
            }
            else if (match.Groups[6].Success)
            {
                yield return new InlinePreviewSpan(match.Groups[6].Value, true, false, false, false, false);
            }
            else if (match.Groups[8].Success)
            {
                yield return new InlinePreviewSpan(match.Groups[8].Value, false, false, false, true, false);
            }
            else if (match.Groups[10].Success)
            {
                yield return new InlinePreviewSpan(match.Groups[10].Value, false, true, false, false, false);
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            yield return new InlinePreviewSpan(text[index..], false, false, false, false, false);
        }
    }
}

public enum PreviewLineKind
{
    Paragraph,
    H1,
    H2,
    H3,
    Bullet,
    Numbered,
    ChecklistOpen,
    ChecklistDone,
    Quote,
    Rule,
    TableRow,
    TableSeparator,
    Blank,
    Muted
}

public readonly record struct PreviewLine(PreviewLineKind Kind, string Text);

public readonly record struct InlinePreviewSpan(
    string Text,
    bool Bold,
    bool Italic,
    bool Code,
    bool Strike,
    bool Wiki);
