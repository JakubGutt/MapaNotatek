using System.Text.RegularExpressions;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class PersonTagService
{
    private static readonly Regex TaskMetadata = new(
        @"\s*<!--\s*people:\s*(?<people>[^>]*)-->\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim().TrimStart('@', '#'))
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(SlugHelper.FromName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Format(IEnumerable<string>? people) =>
        string.Join(", ", Normalize(people));

    public static List<string> Normalize(IEnumerable<string>? people) =>
        people is null ? [] : Parse(string.Join(",", people));

    /// <summary>
    /// Accepts either a stable person identifier or the current display name.
    /// Display names are resolved back to stable slugs so renaming a person does
    /// not break existing assignments.
    /// </summary>
    public static List<string> Resolve(string? value, IEnumerable<Person>? knownPeople)
    {
        var entered = Parse(value);
        if (entered.Count == 0 || knownPeople is null)
        {
            return entered;
        }

        var people = knownPeople.ToList();
        return entered
            .Select(candidate => people.FirstOrDefault(person =>
                    string.Equals(person.Slug, candidate, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(SlugHelper.FromName(person.Name), candidate, StringComparison.OrdinalIgnoreCase))
                ?.Slug ?? candidate)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string StripTaskMetadata(string? text) =>
        TaskMetadata.Replace(text ?? string.Empty, string.Empty).TrimEnd();

    public static List<string> ReadTaskPeople(string? text)
    {
        var match = TaskMetadata.Match(text ?? string.Empty);
        return match.Success ? Parse(match.Groups["people"].Value) : [];
    }

    public static string AppendTaskMetadata(string? text, IEnumerable<string>? people)
    {
        var clean = StripTaskMetadata(text);
        var normalized = Normalize(people);
        return normalized.Count == 0
            ? clean
            : $"{clean} <!-- people: {string.Join(", ", normalized)} -->";
    }

    public static bool Contains(IEnumerable<string>? people, string slug) =>
        people?.Any(value => string.Equals(value, slug, StringComparison.OrdinalIgnoreCase)) == true;
}
