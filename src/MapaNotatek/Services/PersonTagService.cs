using System.Text.RegularExpressions;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class PersonTagService
{
    private static readonly Regex TaskPeopleMetadata = new(
        @"\s*<!--\s*people:\s*(?<people>[^>]*)-->\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TaskPriorityMetadata = new(
        @"\s*<!--\s*priority:\s*(?<priority>-?\d+)\s*-->\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TaskIdMetadata = new(
        @"\s*<!--\s*task-id:\s*(?<id>[a-zA-Z0-9_-]+)\s*-->\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TaskLinksMetadata = new(
        @"\s*<!--\s*links-b64:\s*(?<links>[a-zA-Z0-9_-]+)\s*-->\s*",
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
                ?.Slug)
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> KeepRegistered(
        IEnumerable<string>? assignedPeople,
        IEnumerable<Person>? knownPeople)
    {
        if (assignedPeople is null || knownPeople is null)
        {
            return [];
        }

        var known = knownPeople
            .Select(person => person.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Normalize(assignedPeople)
            .Where(known.Contains)
            .ToList();
    }

    public static string StripTaskMetadata(string? text)
    {
        var clean = TaskPeopleMetadata.Replace(text ?? string.Empty, string.Empty);
        clean = TaskPriorityMetadata.Replace(clean, string.Empty);
        clean = TaskIdMetadata.Replace(clean, string.Empty);
        clean = TaskLinksMetadata.Replace(clean, string.Empty);
        return clean.TrimEnd();
    }

    public static List<string> ReadTaskPeople(string? text)
    {
        var match = TaskPeopleMetadata.Match(text ?? string.Empty);
        return match.Success ? Parse(match.Groups["people"].Value) : [];
    }

    public static int? ReadTaskPriority(string? text)
    {
        var match = TaskPriorityMetadata.Match(text ?? string.Empty);
        return match.Success && int.TryParse(match.Groups["priority"].Value, out var priority)
            ? priority
            : null;
    }

    public static string? ReadTaskId(string? text)
    {
        var match = TaskIdMetadata.Match(text ?? string.Empty);
        return match.Success ? match.Groups["id"].Value : null;
    }

    public static List<ExternalLink> ReadTaskLinks(string? text)
    {
        var match = TaskLinksMetadata.Match(text ?? string.Empty);
        return match.Success
            ? ExternalLinkService.DecodeTaskMetadata(match.Groups["links"].Value)
            : [];
    }

    public static string AppendTaskMetadata(
        string? text,
        IEnumerable<string>? people,
        int? priority = null,
        string? taskId = null,
        IEnumerable<ExternalLink>? externalLinks = null)
    {
        var clean = StripTaskMetadata(text);
        var normalized = Normalize(people);
        var idMetadata = string.IsNullOrWhiteSpace(taskId) ? string.Empty : $" <!-- task-id: {taskId.Trim()} -->";
        var priorityMetadata = priority.HasValue ? $" <!-- priority: {priority.Value} -->" : string.Empty;
        var links = ExternalLinkService.EncodeTaskMetadata(externalLinks);
        var linksMetadata = links.Length == 0 ? string.Empty : $" <!-- links-b64: {links} -->";
        var peopleMetadata = normalized.Count == 0
            ? string.Empty
            : $" <!-- people: {string.Join(", ", normalized)} -->";
        return clean + idMetadata + priorityMetadata + linksMetadata + peopleMetadata;
    }

    public static bool Contains(IEnumerable<string>? people, string slug) =>
        people?.Any(value => string.Equals(value, slug, StringComparison.OrdinalIgnoreCase)) == true;
}
