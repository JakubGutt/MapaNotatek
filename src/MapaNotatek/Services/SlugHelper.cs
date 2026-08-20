using System.Globalization;
using System.Text;

namespace MapaNotatek.Services;

public static class SlugHelper
{
    public static string FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "element";
        }

        var normalized = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var previousHyphen = false;

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var mapped = MapPolish(ch);
            if (char.IsLetterOrDigit(mapped))
            {
                builder.Append(mapped);
                previousHyphen = false;
            }
            else if (!previousHyphen)
            {
                builder.Append('-');
                previousHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "element" : slug;
    }

    public static string Unique(string baseSlug, IEnumerable<string> existing)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseSlug))
        {
            return baseSlug;
        }

        var index = 2;
        while (used.Contains($"{baseSlug}-{index}"))
        {
            index++;
        }

        return $"{baseSlug}-{index}";
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static char MapPolish(char ch) => ch switch
    {
        'ł' or 'Ł' => 'l',
        _ => ch
    };
}
