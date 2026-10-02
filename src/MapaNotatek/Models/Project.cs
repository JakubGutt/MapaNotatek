namespace MapaNotatek.Models;

public sealed class Project
{
    private ProjectItemType _itemType = ProjectItemType.Project;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> People { get; set; } = [];
    /// <summary>Explicit system memberships. They are never inherited from the parent.</summary>
    public List<string> SystemIds { get; set; } = [];
    public List<ChecklistItem> Checklist { get; set; } = [];
    public bool IsArchived { get; set; }
    /// <summary>Parent project/folder id; null/empty = root.</summary>
    public string? ParentId { get; set; }
    /// <summary>Role in the product architecture or an organizational/legacy role.</summary>
    public ProjectItemType ItemType
    {
        get => _itemType;
        set => _itemType = value;
    }

    /// <summary>Compatibility property for existing code and libraries.</summary>
    public bool IsFolder
    {
        get => ItemType == ProjectItemType.Folder;
        set
        {
            if (value)
            {
                ItemType = ProjectItemType.Folder;
            }
            else if (ItemType == ProjectItemType.Folder)
            {
                ItemType = ProjectItemType.Project;
            }
        }
    }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
    public string FilePath { get; set; } = string.Empty;
    internal string PersistedContentHash { get; set; } = string.Empty;
}
