namespace MapaNotatek.Models;

/// <summary>
/// A user-managed reference to an external resource. MapaNotatek stores and
/// copies these values, but deliberately never opens or downloads them.
/// </summary>
public sealed class ExternalLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
