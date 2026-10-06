namespace MapaNotatek.Models;

public sealed class ChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public List<string> People { get; set; } = [];
    public int? Priority { get; set; }
    public List<ExternalLink> ExternalLinks { get; set; } = [];

    // Runtime-only origin metadata used to keep an indexed Markdown task in its
    // original body position. It is deliberately not part of the persisted model.
    internal int SourceLineIndex { get; set; } = -1;
    internal string? SourceLine { get; set; }
}
