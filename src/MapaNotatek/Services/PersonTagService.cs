using System.Text.RegularExpressions;

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
