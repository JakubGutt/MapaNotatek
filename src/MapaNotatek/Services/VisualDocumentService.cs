using System.Text;
using System.Text.RegularExpressions;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

/// <summary>
/// Loss-conscious adapter between portable Markdown and the block-oriented visual editor.
/// It intentionally supports the same practical subset as the preview and exporters.
/// </summary>
public static class VisualDocumentService
{
    private static readonly Regex Heading = new(@"^(?<level>#{1,3})\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex Checklist = new(@"^\s*[-*]\s+\[(?<state>[ xX])\]\s*(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^\s*[-*]\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex Numbered = new(@"^\s*\d+[.)]\s+(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex Image = new(@"^\s*!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)\s*$", RegexOptions.Compiled);
    private static readonly Regex TableSeparator = new(
        @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$",
        RegexOptions.Compiled);
    private static readonly Regex InlineMarkers = new(
        @"(!?\[\[?|\]\]?\([^)]*\)|\]\]|\*\*|~~|`|(?<!\*)\*(?!\*))",
        RegexOptions.Compiled);

    public static List<DocumentBlock> Parse(string? markdown)
    {
        var text = Normalize(markdown ?? string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [NewParagraph()];
        }

        var lines = text.Split('\n');
        var result = new List<DocumentBlock>();
        var index = 0;
        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            var line = lines[index];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var language = line.Trim()[3..].Trim();
                var body = new List<string>();
                index++;
                while (index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    body.Add(lines[index]);
                    index++;
                }

                if (index < lines.Length)
                {
                    index++;
                }

                result.Add(new DocumentBlock
                {
                    Kind = DocumentBlockKind.Code,
                    Language = language,
                    Text = string.Join("\n", body)
                });
                continue;
            }

            if (LooksLikeTableRow(line) && index + 1 < lines.Length && TableSeparator.IsMatch(lines[index + 1]))
            {
                var table = new DocumentBlock { Kind = DocumentBlockKind.Table };
                table.Cells.Add(SplitTableRow(line));
                index += 2;
                while (index < lines.Length && LooksLikeTableRow(lines[index]) && !TableSeparator.IsMatch(lines[index]))
                {
                    table.Cells.Add(SplitTableRow(lines[index]));
                    index++;
                }

                NormalizeTable(table);
                result.Add(table);
                continue;
            }

            var image = Image.Match(line);
            if (image.Success)
            {
                result.Add(new DocumentBlock
                {
                    Kind = DocumentBlockKind.Image,
                    Text = image.Groups["alt"].Value,
                    ImagePath = image.Groups["path"].Value.Trim()
                });
                index++;
                continue;
            }

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                result.Add(new DocumentBlock
                {
                    Kind = heading.Groups["level"].Value.Length switch
                    {
                        1 => DocumentBlockKind.Heading1,
                        2 => DocumentBlockKind.Heading2,
                        _ => DocumentBlockKind.Heading3
                    },
                    Text = heading.Groups["text"].Value
                });
                index++;
                continue;
            }

            var checklist = Checklist.Match(line);
            if (checklist.Success)
            {
                result.Add(new DocumentBlock
                {
                    Kind = DocumentBlockKind.Checklist,
                    IsChecked = !string.Equals(checklist.Groups["state"].Value, " ", StringComparison.Ordinal),
                    Text = checklist.Groups["text"].Value
                });
                index++;
                continue;
            }

            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                result.Add(new DocumentBlock { Kind = DocumentBlockKind.Bullet, Text = bullet.Groups["text"].Value });
                index++;
                continue;
            }

            var numbered = Numbered.Match(line);
            if (numbered.Success)
            {
                result.Add(new DocumentBlock { Kind = DocumentBlockKind.Numbered, Text = numbered.Groups["text"].Value });
                index++;
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal) || string.Equals(line.Trim(), ">", StringComparison.Ordinal))
            {
                result.Add(new DocumentBlock
                {
                    Kind = DocumentBlockKind.Quote,
                    Text = line.Length > 1 ? line[1..].TrimStart() : string.Empty
                });
                index++;
                continue;
            }

            if (IsRule(line))
            {
                result.Add(new DocumentBlock { Kind = DocumentBlockKind.Rule });
                index++;
                continue;
            }

            var paragraph = new List<string> { line };
            index++;
            while (index < lines.Length &&
                   !string.IsNullOrWhiteSpace(lines[index]) &&
                   !StartsStructuredBlock(lines, index))
            {
                paragraph.Add(lines[index]);
                index++;
            }

            result.Add(new DocumentBlock
            {
                Kind = DocumentBlockKind.Paragraph,
                Text = string.Join("\n", paragraph)
            });
        }

        return result.Count == 0 ? [NewParagraph()] : result;
    }

    public static string Serialize(IEnumerable<DocumentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var materialized = blocks.ToList();
        var builder = new StringBuilder();
        DocumentBlockKind? previousKind = null;
        foreach (var block in materialized)
        {
            var rendered = RenderBlock(block);
            if (rendered.Length == 0 && block.Kind != DocumentBlockKind.Paragraph)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                var compactList = IsListKind(previousKind) && IsListKind(block.Kind);
                builder.Append(compactList ? '\n' : "\n\n");
            }

            builder.Append(rendered);
            previousKind = block.Kind;
        }

        return builder.ToString().TrimEnd();
    }

    public static IReadOnlyList<DocumentOutlineEntry> BuildOutline(IEnumerable<DocumentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return blocks
            .Where(block => block.Kind is DocumentBlockKind.Heading1 or DocumentBlockKind.Heading2 or DocumentBlockKind.Heading3)
            .Select(block => new DocumentOutlineEntry(
                block.RuntimeId,
                StripInlineFormatting(block.Text).Trim(),
                block.Kind switch
                {
                    DocumentBlockKind.Heading1 => 1,
                    DocumentBlockKind.Heading2 => 2,
                    _ => 3
                }))
            .Where(entry => entry.Title.Length > 0)
            .ToList();
    }

    public static DocumentBlock NewParagraph(string text = "") =>
        new() { Kind = DocumentBlockKind.Paragraph, Text = text };

    public static DocumentBlock NewTable(int rows = 3, int columns = 3)
    {
        rows = Math.Clamp(rows, 1, 50);
        columns = Math.Clamp(columns, 1, 20);
        var table = new DocumentBlock { Kind = DocumentBlockKind.Table };
        for (var row = 0; row < rows; row++)
        {
            var cells = new List<string>(columns);
            for (var column = 0; column < columns; column++)
            {
                cells.Add(row == 0 ? $"Kolumna {column + 1}" : string.Empty);
            }

            table.Cells.Add(cells);
        }

        return table;
    }

    public static void NormalizeTable(DocumentBlock table)
    {
        if (table.Cells.Count == 0)
        {
            table.Cells.Add([string.Empty]);
        }

        var columns = Math.Clamp(table.Cells.Max(row => row.Count), 1, 20);
        foreach (var row in table.Cells)
        {
            while (row.Count < columns)
            {
                row.Add(string.Empty);
            }

            if (row.Count > columns)
            {
                row.RemoveRange(columns, row.Count - columns);
            }
        }
    }

    private static bool StartsStructuredBlock(IReadOnlyList<string> lines, int index)
    {
        var line = lines[index];
        return line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
               Heading.IsMatch(line) ||
               Checklist.IsMatch(line) ||
               Bullet.IsMatch(line) ||
               Numbered.IsMatch(line) ||
               Image.IsMatch(line) ||
               line.StartsWith('>') ||
               IsRule(line) ||
               (LooksLikeTableRow(line) && index + 1 < lines.Count && TableSeparator.IsMatch(lines[index + 1]));
    }

    private static string RenderBlock(DocumentBlock block) => block.Kind switch
    {
        DocumentBlockKind.Heading1 => "# " + block.Text.TrimEnd(),
        DocumentBlockKind.Heading2 => "## " + block.Text.TrimEnd(),
        DocumentBlockKind.Heading3 => "### " + block.Text.TrimEnd(),
        DocumentBlockKind.Bullet => "- " + block.Text.TrimEnd(),
        DocumentBlockKind.Numbered => "1. " + block.Text.TrimEnd(),
        DocumentBlockKind.Checklist => $"- [{(block.IsChecked ? "x" : " ")}] {block.Text.TrimEnd()}",
        DocumentBlockKind.Quote => RenderPrefixedLines(block.Text, "> "),
        DocumentBlockKind.Code => $"```{block.Language.Trim()}\n{block.Text.TrimEnd()}\n```",
        DocumentBlockKind.Rule => "---",
        DocumentBlockKind.Image => $"![{EscapeImageAlt(block.Text)}]({block.ImagePath.Trim()})",
        DocumentBlockKind.Table => RenderTable(block),
        _ => block.Text.TrimEnd()
    };

    private static string RenderTable(DocumentBlock block)
    {
        NormalizeTable(block);
        var columns = block.Cells[0].Count;
        var rows = new List<string>
        {
            RenderTableRow(block.Cells[0]),
            RenderTableRow(Enumerable.Repeat("---", columns))
        };
        rows.AddRange(block.Cells.Skip(1).Select(RenderTableRow));
        return string.Join("\n", rows);
    }

    private static string RenderTableRow(IEnumerable<string> cells) =>
        "| " + string.Join(" | ", cells.Select(cell => cell.Replace("|", "\\|", StringComparison.Ordinal).Trim())) + " |";

    private static List<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        var cells = new List<string>();
        var cell = new StringBuilder();
        for (var index = 0; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (character == '\\' && index + 1 < trimmed.Length && trimmed[index + 1] == '|')
            {
                cell.Append('|');
                index++;
            }
            else if (character == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(character);
            }
        }

        cells.Add(cell.ToString().Trim());
        return cells;
    }

    private static bool LooksLikeTableRow(string line) => line.Count(character => character == '|') >= 1;

    private static bool IsRule(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 &&
               (trimmed.All(character => character == '-') ||
                trimmed.All(character => character == '*') ||
                trimmed.All(character => character == '_'));
    }

    private static bool IsListKind(DocumentBlockKind? kind) => kind is
        DocumentBlockKind.Bullet or DocumentBlockKind.Numbered or DocumentBlockKind.Checklist;

    private static string RenderPrefixedLines(string text, string prefix) =>
        string.Join("\n", Normalize(text).Split('\n').Select(line => prefix + line));

    private static string EscapeImageAlt(string text) => text.Replace("]", "\\]", StringComparison.Ordinal).Trim();

    private static string StripInlineFormatting(string text) =>
        InlineMarkers.Replace(text, string.Empty).Replace("\\|", "|", StringComparison.Ordinal);

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
