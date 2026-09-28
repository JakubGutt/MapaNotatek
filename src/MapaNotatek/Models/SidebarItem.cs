namespace MapaNotatek.Models;

public sealed class SidebarItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool IsProject { get; init; }
    public bool IsFolder { get; init; }
    public ProjectItemType ItemType { get; init; } = ProjectItemType.Project;

    public override string ToString() => Title;
}
