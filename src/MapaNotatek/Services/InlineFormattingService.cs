namespace MapaNotatek.Services;

/// <summary>
/// Applies portable inline Markdown markers to a real text selection. It never
/// invents placeholder copy: an empty selection is left untouched.
/// </summary>
public static class InlineFormattingService
{
    public static InlineFormatEdit Toggle(
        string? source,
        int selectionStart,
        int selectionEnd,
        string before,
        string after)
    {
        var text = source ?? string.Empty;
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        if (end <= start)
        {
            return new InlineFormatEdit(text, start, end, Changed: false);
        }

        var selected = text[start..end];
        var selectedHasDifferentAsteriskWrapper = before == "*" && after == "*" &&
                                                   selected.StartsWith("**", StringComparison.Ordinal) &&
                                                   selected.EndsWith("**", StringComparison.Ordinal);
        if (!selectedHasDifferentAsteriskWrapper &&
            selected.Length >= before.Length + after.Length &&
            selected.StartsWith(before, StringComparison.Ordinal) &&
            selected.EndsWith(after, StringComparison.Ordinal))
        {
            var inner = selected[before.Length..(selected.Length - after.Length)];
            var next = text[..start] + inner + text[end..];
            return new InlineFormatEdit(next, start, start + inner.Length, Changed: true);
        }

        var asteriskBelongsToBold = before == "*" && after == "*" &&
                                      ((start > before.Length && text[start - before.Length - 1] == '*') ||
                                       (end + after.Length < text.Length && text[end + after.Length] == '*'));
        var surrounded = !asteriskBelongsToBold &&
                         start >= before.Length &&
                         end + after.Length <= text.Length &&
                         string.Equals(text[(start - before.Length)..start], before, StringComparison.Ordinal) &&
                         string.Equals(text[end..(end + after.Length)], after, StringComparison.Ordinal);
        if (surrounded)
        {
            var next = text[..(start - before.Length)] + selected + text[(end + after.Length)..];
            var nextStart = start - before.Length;
            return new InlineFormatEdit(next, nextStart, nextStart + selected.Length, Changed: true);
        }

        var formatted = before + selected + after;
        return new InlineFormatEdit(
            text[..start] + formatted + text[end..],
            start + before.Length,
            start + before.Length + selected.Length,
            Changed: true);
    }
}

public readonly record struct InlineFormatEdit(
    string Text,
    int SelectionStart,
    int SelectionEnd,
    bool Changed);
