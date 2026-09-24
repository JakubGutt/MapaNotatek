using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class FrontMatter
{
    private static readonly Regex ChecklistLine = new(
        @"^(?<prefix>\s*[-*]\s+\[)(?<state>[ xX])(?<suffix>\]\s*)(?<text>.*)$",
        RegexOptions.Compiled);

    public static ParsedDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new ParsedDocument();
        var lines = NormalizeNewLines(text).Split('\n');
        var index = 0;

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            index = 1;
            while (index < lines.Length && lines[index].Trim() != "---")
            {
                var line = lines[index];
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    var key = line[..colon].Trim();
                    var value = line[(colon + 1)..].Trim();
                    result.Fields[key] = value;
                }

                index++;
            }

            if (index >= lines.Length)
            {
                throw new FormatException("Sekcja front matter nie ma znacznika zamykającego ---.");
            }

            index++;
        }

        var bodyLines = lines[index..].ToList();
        TrimOuterBlankLines(bodyLines);

        if (bodyLines.Count > 0 && bodyLines[0].StartsWith("# ", StringComparison.Ordinal))
        {
            result.Title = bodyLines[0][2..].Trim();
            bodyLines.RemoveAt(0);
            while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[0]))
            {
                bodyLines.RemoveAt(0);
            }
        }

        TrimTrailingBlankLines(bodyLines);
        for (var lineIndex = 0; lineIndex < bodyLines.Count; lineIndex++)
        {
            if (!TryReadChecklistLine(bodyLines[lineIndex], out var isDone, out var itemText))
            {
                continue;
            }

            result.Checklist.Add(new ChecklistItem
            {
                IsDone = isDone,
                Text = itemText,
                SourceLineIndex = lineIndex,
                SourceLine = bodyLines[lineIndex]
            });
        }

        result.Body = string.Join("\n", bodyLines);
        return result;
    }

    public static string WriteNote(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var builder = new StringBuilder();
        WriteHeader(
            builder,
            note.Id,
            "note",
            string.Join(", ", note.Tags),
            note.Created,
            note.Modified,
            archived: null,
            slug: null,
            parentId: null,
            isFolder: false);
        builder.Append("# ").AppendLine(note.Title);
        builder.AppendLine();

        note.Body = MergeChecklistIntoBody(note.Body, note.Checklist);
        if (!string.IsNullOrWhiteSpace(note.Body))
        {
            builder.AppendLine(note.Body.TrimEnd());
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string WriteProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var builder = new StringBuilder();
        WriteHeader(
            builder,
            project.Id,
            "project",
            project.Slug,
            project.Created,
            project.Modified,
            archived: project.IsArchived,
            project.Slug,
            project.ParentId,
            project.IsFolder);
        builder.Append("# ").AppendLine(project.Name);
        builder.AppendLine();

        project.Description = MergeChecklistIntoBody(project.Description, project.Checklist);
        if (!string.IsNullOrWhiteSpace(project.Description))
        {
            builder.AppendLine(project.Description.TrimEnd());
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static List<string> SplitTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim().TrimStart('#').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static DateTimeOffset ReadDate(string? value, DateTimeOffset fallback)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : fallback;
    }

    public static bool ReadBool(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    private static string MergeChecklistIntoBody(string? body, IList<ChecklistItem> items)
    {
        var normalized = NormalizeNewLines(body ?? string.Empty).TrimEnd('\n');
        var lines = normalized.Length == 0 ? new List<string>() : normalized.Split('\n').ToList();
        var claimedLines = new HashSet<int>();

        foreach (var item in items)
        {
            var sourceIndex = FindOriginalSourceLine(lines, claimedLines, item);
            if (sourceIndex >= 0)
            {
                var updatedLine = FormatChecklistLine(lines[sourceIndex], item);
                lines[sourceIndex] = updatedLine;
                claimedLines.Add(sourceIndex);
                BindToLine(item, sourceIndex, updatedLine);
                continue;
            }

            if (item.SourceLine is not null)
            {
                // The user changed or removed the source line directly in Body.
                // Body is authoritative, so the stale indexed item is not appended.
                continue;
            }

            var matchingBodyLine = FindMatchingUnclaimedChecklistLine(lines, claimedLines, item.Text);
            if (matchingBodyLine >= 0)
            {
                var updatedLine = FormatChecklistLine(lines[matchingBodyLine], item);
                lines[matchingBodyLine] = updatedLine;
                claimedLines.Add(matchingBodyLine);
                BindToLine(item, matchingBodyLine, updatedLine);
                continue;
            }

            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            var newLine = $"- [{(item.IsDone ? 'x' : ' ')}] {item.Text}";
            lines.Add(newLine);
            var newIndex = lines.Count - 1;
            claimedLines.Add(newIndex);
            BindToLine(item, newIndex, newLine);
        }

        return string.Join("\n", lines).TrimEnd();
    }

    private static int FindOriginalSourceLine(
        IReadOnlyList<string> lines,
        ISet<int> claimedLines,
        ChecklistItem item)
    {
        if (item.SourceLine is null)
        {
            return -1;
        }

        if (item.SourceLineIndex >= 0 && item.SourceLineIndex < lines.Count &&
            !claimedLines.Contains(item.SourceLineIndex) &&
            string.Equals(lines[item.SourceLineIndex], item.SourceLine, StringComparison.Ordinal))
        {
            return item.SourceLineIndex;
        }

        var match = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            if (claimedLines.Contains(index) ||
                !string.Equals(lines[index], item.SourceLine, StringComparison.Ordinal))
            {
                continue;
            }

            if (match >= 0)
            {
                return -1;
            }

            match = index;
        }

        return match;
    }

    private static int FindMatchingUnclaimedChecklistLine(
        IReadOnlyList<string> lines,
        ISet<int> claimedLines,
        string text)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (claimedLines.Contains(index) ||
                !TryReadChecklistLine(lines[index], out _, out var existingText) ||
                !string.Equals(existingText, text, StringComparison.Ordinal))
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static string FormatChecklistLine(string originalLine, ChecklistItem item)
    {
        var match = ChecklistLine.Match(originalLine);
        if (!match.Success)
        {
            return $"- [{(item.IsDone ? 'x' : ' ')}] {item.Text}";
        }

        return match.Groups["prefix"].Value +
               (item.IsDone ? "x" : " ") +
               match.Groups["suffix"].Value +
               item.Text;
    }

    private static bool TryReadChecklistLine(string line, out bool isDone, out string text)
    {
        var match = ChecklistLine.Match(line);
        if (!match.Success)
        {
            isDone = false;
            text = string.Empty;
            return false;
        }

        isDone = !string.Equals(match.Groups["state"].Value, " ", StringComparison.Ordinal);
        text = match.Groups["text"].Value.TrimEnd();
        return true;
    }

    private static void BindToLine(ChecklistItem item, int lineIndex, string line)
    {
        item.SourceLineIndex = lineIndex;
        item.SourceLine = line;
    }

    private static string NormalizeNewLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static void TrimOuterBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        TrimTrailingBlankLines(lines);
    }

    private static void TrimTrailingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static void WriteHeader(
        StringBuilder builder,
        string id,
        string type,
        string tags,
        DateTimeOffset created,
        DateTimeOffset modified,
        bool? archived,
        string? slug,
        string? parentId,
        bool isFolder)
    {
        builder.AppendLine("---");
        builder.Append("id: ").AppendLine(id);
        builder.Append("type: ").AppendLine(type);
        if (!string.IsNullOrWhiteSpace(slug) && type == "project")
        {
            builder.Append("slug: ").AppendLine(slug);
        }

        if (type == "project" && isFolder)
        {
            builder.AppendLine("kind: folder");
        }

        if (type == "project" && !string.IsNullOrWhiteSpace(parentId))
        {
            builder.Append("parent: ").AppendLine(parentId);
        }

        builder.Append("tags: ").AppendLine(tags);
        if (archived.HasValue)
        {
            builder.Append("archived: ").AppendLine(archived.Value ? "true" : "false");
        }

        builder.Append("created: ").AppendLine(created.ToString("O", CultureInfo.InvariantCulture));
        builder.Append("modified: ").AppendLine(modified.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendLine("---");
    }

    public sealed class ParsedDocument
    {
        public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public List<ChecklistItem> Checklist { get; } = [];

        public string? this[string key] => Fields.TryGetValue(key, out var value) ? value : null;
    }
}
