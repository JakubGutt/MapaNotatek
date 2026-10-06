namespace MapaNotatek.Models;

public sealed class Note
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> People { get; set; } = [];
    public List<ExternalLink> ExternalLinks { get; set; } = [];
    /// <summary>Stable ids of explicitly related notes. The UI treats the relation as undirected.</summary>
    public List<string> RelatedNoteIds { get; set; } = [];
    public List<ChecklistItem> Checklist { get; set; } = [];
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
    public string FilePath { get; set; } = string.Empty;
    internal string PersistedContentHash { get; set; } = string.Empty;
}
