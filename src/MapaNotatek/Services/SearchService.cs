using MapaNotatek.Models;
using System.Text.RegularExpressions;

namespace MapaNotatek.Services;

public static class SearchService
{
    private static readonly Regex QueryToken = new(
        """(?<exclude>-)?(?:(?<field>[\p{L}]+):)?(?:"(?<quoted>[^"]+)"|(?<word>\S+))""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool Matches(string query, Project project)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Parse(query).All(term => term.Exclude != Matches(project, term));
    }

    public static bool Matches(string query, Note note)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Parse(query).All(term => term.Exclude != Matches(note, term));
    }

    public static string HelpText =>
        "Filtry: title:, body:, tag:, project:, type:, has:task. Cudzysłów szuka całej frazy, a - wyklucza.";

    private static bool Matches(Note note, SearchTerm term) => term.Field switch
    {
        "title" or "tytuł" => Contains(note.Title, term.Value),
        "body" or "treść" => Contains(note.Body, term.Value),
        "tag" => note.Tags.Any(tag => Contains(tag, term.Value)),
        "project" or "projekt" => note.Tags.Any(tag => Contains(tag, term.Value)),
        "type" or "typ" => Contains("note notatka", term.Value),
        "has" or "ma" when IsTaskValue(term.Value) => note.Checklist.Count > 0,
        "has" or "ma" => false,
        _ => Contains(note.Title, term.Value) ||
             Contains(note.Body, term.Value) ||
             note.Tags.Any(tag => Contains(tag, term.Value))
    };

    private static bool Matches(Project project, SearchTerm term) => term.Field switch
    {
        "title" or "tytuł" => Contains(project.Name, term.Value),
        "body" or "treść" => Contains(project.Description, term.Value),
        "tag" or "project" or "projekt" => Contains(project.Slug, term.Value),
        "type" or "typ" => Contains(project.ItemType.SearchText(), term.Value),
        "has" or "ma" when IsTaskValue(term.Value) => project.Checklist.Count > 0,
        "has" or "ma" => false,
        _ => Contains(project.Name, term.Value) ||
             Contains(project.Description, term.Value) ||
             Contains(project.Slug, term.Value) ||
             Contains(project.ItemType.SearchText(), term.Value)
    };

    private static IReadOnlyList<SearchTerm> Parse(string query)
    {
        var terms = new List<SearchTerm>();
        foreach (Match match in QueryToken.Matches(query))
        {
            var value = match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["word"].Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            terms.Add(new SearchTerm(
                match.Groups["field"].Value.ToLowerInvariant(),
                value,
                match.Groups["exclude"].Success));
        }

        return terms;
    }

    private static bool IsTaskValue(string value) =>
        value.Equals("task", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("zadanie", StringComparison.CurrentCultureIgnoreCase) ||
        value.Equals("tasks", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? text, string query) =>
        !string.IsNullOrEmpty(text) &&
        text.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private sealed record SearchTerm(string Field, string Value, bool Exclude);
}
