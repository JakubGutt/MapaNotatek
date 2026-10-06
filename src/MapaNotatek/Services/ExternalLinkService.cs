using System.Text;
using System.Text.Json;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class ExternalLinkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ExternalLink Create(string? label, string? url)
    {
        var normalizedUrl = NormalizeUrl(url);
        var normalizedLabel = string.IsNullOrWhiteSpace(label)
            ? new Uri(normalizedUrl).Host
            : label.Trim();
        return new ExternalLink
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = normalizedLabel,
            Url = normalizedUrl
        };
    }

    public static string NormalizeUrl(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new FormatException("Link musi być pełnym adresem http:// lub https://.");
        }

        return uri.AbsoluteUri;
    }

    public static List<ExternalLink> Normalize(IEnumerable<ExternalLink>? links)
    {
        var result = new List<ExternalLink>();
        foreach (var link in links ?? [])
        {
            try
            {
                var normalized = Create(link.Label, link.Url);
                normalized.Id = string.IsNullOrWhiteSpace(link.Id) ? normalized.Id : link.Id.Trim();
                result.Add(normalized);
            }
            catch (FormatException)
            {
                // Invalid values are never made actionable. Preserve data safety by
                // skipping malformed imported metadata rather than launching it.
            }
        }

        return result
            .DistinctBy(link => link.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Serialize(IEnumerable<ExternalLink>? links)
    {
        var normalized = Normalize(links);
        return normalized.Count == 0 ? string.Empty : JsonSerializer.Serialize(normalized, JsonOptions);
    }

    public static List<ExternalLink> Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<List<ExternalLink>>(value, JsonOptions));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string EncodeTaskMetadata(IEnumerable<ExternalLink>? links)
    {
        var json = Serialize(links);
        return json.Length == 0
            ? string.Empty
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
    }

    public static List<ExternalLink> DecodeTaskMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            return Deserialize(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
        }
        catch (FormatException)
        {
            return [];
        }
    }

    public static List<ExternalLink> Clone(IEnumerable<ExternalLink>? links) =>
        Normalize(links).Select(link => new ExternalLink
        {
            Id = link.Id,
            Label = link.Label,
            Url = link.Url
        }).ToList();
}
