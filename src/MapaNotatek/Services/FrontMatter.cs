using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class FrontMatter
{
    private static readonly Regex ChecklistLine = new(
        @"^\s*[-*]\s+\[([ xX])\]\s*(.*)$",
        RegexOptions.Compiled);

    public static ParsedDocument Parse(string text)
    {
        var result = new ParsedDocument();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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

            if (index < lines.Length && lines[index].Trim() == "---")
            {
                index++;
            }
        }

        var bodyLines = new List<string>();
        for (; index < lines.Length; index++)
        {
            var match = ChecklistLine.Match(lines[index]);
            if (match.Success)
            {
                result.Checklist.Add(new ChecklistItem
                {
                    IsDone = !string.Equals(match.Groups[1].Value, " ", StringComparison.Ordinal),
                    Text = match.Groups[2].Value.TrimEnd()
                });
            }
            else
            {
                bodyLines.Add(lines[index]);
            }
        }

        while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[0]))
        {
            bodyLines.RemoveAt(0);
        }

        while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[^1]))
        {
            bodyLines.RemoveAt(bodyLines.Count - 1);
        }

        if (bodyLines.Count > 0 && bodyLines[0].StartsWith("# ", StringComparison.Ordinal))
        {
            result.Title = bodyLines[0][2..].Trim();
            bodyLines.RemoveAt(0);
            while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[0]))
            {
                bodyLines.RemoveAt(0);
            }
        }

        result.Body = string.Join("\n", bodyLines);
        return result;
    }

    public static string WriteNote(Note note)
    {
        var builder = new StringBuilder();
        WriteHeader(builder, note.Id, "note", string.Join(", ", note.Tags), note.Created, note.Modified, archived: null, slug: null);
        builder.Append("# ").AppendLine(note.Title);
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(note.Body))
        {
            builder.AppendLine(note.Body.TrimEnd());
            builder.AppendLine();
        }

        WriteChecklist(builder, note.Checklist);
        return builder.ToString();
    }

    public static string WriteProject(Project project)
    {
        var builder = new StringBuilder();
        WriteHeader(builder, project.Id, "project", project.Slug, project.Created, project.Modified, project.IsArchived, project.Slug);
        builder.Append("# ").AppendLine(project.Name);
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(project.Description))
        {
            builder.AppendLine(project.Description.TrimEnd());
            builder.AppendLine();
        }

        WriteChecklist(builder, project.Checklist);
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
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    public static bool ReadBool(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

    private static void WriteHeader(
        StringBuilder builder,
        string id,
        string type,
        string tags,
        DateTimeOffset created,
        DateTimeOffset modified,
        bool? archived,
        string? slug)
    {
        builder.AppendLine("---");
        builder.Append("id: ").AppendLine(id);
        builder.Append("type: ").AppendLine(type);
        if (!string.IsNullOrWhiteSpace(slug) && type == "project")
        {
            builder.Append("slug: ").AppendLine(slug);
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

    private static void WriteChecklist(StringBuilder builder, IEnumerable<ChecklistItem> items)
    {
        foreach (var item in items)
        {
            builder.Append("- [").Append(item.IsDone ? 'x' : ' ').Append("] ").AppendLine(item.Text);
        }
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
