namespace MapaNotatek.Models;

public sealed class Project
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ChecklistItem> Checklist { get; set; } = [];
    public bool IsArchived { get; set; }
    /// <summary>Parent project/folder id; null/empty = root.</summary>
    public string? ParentId { get; set; }
    /// <summary>Folder container (no checklist emphasis); still stored as type project.</summary>
    public bool IsFolder { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
    public string FilePath { get; set; } = string.Empty;
    internal string PersistedContentHash { get; set; } = string.Empty;
}
