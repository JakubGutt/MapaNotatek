using System.Text.RegularExpressions;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

/// <summary>
/// Keeps Enter behaviour for list-like content consistent in both the cell
/// editor and the optional Markdown source view.
/// </summary>
public static class ListEditingService
{
    private static readonly Regex MarkdownListPrefix = new(
        @"^(?<indent>\s*)(?<marker>(?:[-*]\s+\[[ xX]\]\s+)|(?:[-*]\s+)|(?:\d+[.)]\s+))(?<content>.*)$",
        RegexOptions.Compiled);

    public static BlockEnterEdit SplitBlock(
        DocumentBlock source,
        string? editorText,
        int selectionStart,
        int selectionEnd)
    {
        ArgumentNullException.ThrowIfNull(source);
        var text = editorText ?? string.Empty;
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        var listKind = IsListKind(source.Kind);

        // A second Enter on an empty item ends the list, like Word/Google Docs.
        if (listKind && string.IsNullOrWhiteSpace(text))
        {
            return new BlockEnterEdit(
                DocumentBlockKind.Paragraph,
                string.Empty,
                CurrentIsChecked: false,
                FollowingBlock: null);
        }

        var followingKind = listKind ? source.Kind : DocumentBlockKind.Paragraph;
        return new BlockEnterEdit(
            source.Kind,
            text[..start],
            source.IsChecked,
            new DocumentBlock
            {
                Kind = followingKind,
                Text = text[end..],
                // A newly-created task always starts open, even if the
                // preceding task had already been completed.
                IsChecked = false,
                People = []
            });
    }

    public static TextEnterEdit ContinueMarkdownList(
        string? source,
        int selectionStart,
        int selectionEnd)
    {
        var text = source ?? string.Empty;
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var prefixSegment = text[lineStart..start];
        var match = MarkdownListPrefix.Match(prefixSegment);
        if (!match.Success)
        {
            return new TextEnterEdit(text, start, Changed: false);
        }

        var content = match.Groups["content"].Value;
        if (content.Length == 0)
        {
            var next = text[..lineStart] + text[end..];
            return new TextEnterEdit(next, lineStart, Changed: true);
        }

        var indent = match.Groups["indent"].Value;
        var marker = match.Groups["marker"].Value;
        var nextMarker = NormalizeNextMarker(marker);
        var insertion = "\n" + indent + nextMarker;
        var updated = text[..start] + insertion + text[end..];
        return new TextEnterEdit(updated, start + insertion.Length, Changed: true);
    }

    private static bool IsListKind(DocumentBlockKind kind) => kind is
        DocumentBlockKind.Bullet or DocumentBlockKind.Numbered or DocumentBlockKind.Checklist;

    private static string NormalizeNextMarker(string marker)
    {
        if (Regex.IsMatch(marker, @"^[-*]\s+\[[ xX]\]\s+$"))
        {
            return marker[..2] + "[ ] ";
        }

        var numbered = Regex.Match(marker, @"^(?<number>\d+)(?<separator>[.)])\s+$");
        if (numbered.Success && int.TryParse(numbered.Groups["number"].Value, out var number))
        {
            return $"{number + 1}{numbered.Groups["separator"].Value} ";
        }

        return marker;
    }
}

public readonly record struct BlockEnterEdit(
    DocumentBlockKind CurrentKind,
    string CurrentText,
    bool CurrentIsChecked,
    DocumentBlock? FollowingBlock);

public readonly record struct TextEnterEdit(string Text, int CaretIndex, bool Changed);
