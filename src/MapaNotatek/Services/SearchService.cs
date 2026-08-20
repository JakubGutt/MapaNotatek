using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class SearchService
{
    public static bool Matches(string query, Project project)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Contains(project.Name, query)
            || Contains(project.Description, query)
            || Contains(project.Slug, query);
    }

    public static bool Matches(string query, Note note)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (Contains(note.Title, query) || Contains(note.Body, query))
        {
            return true;
        }

        return note.Tags.Any(tag => Contains(tag, query));
    }

    private static bool Contains(string? text, string query) =>
        !string.IsNullOrEmpty(text) &&
        text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
