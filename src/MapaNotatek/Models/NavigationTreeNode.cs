namespace MapaNotatek.Models;

public sealed class NavigationTreeNode
{
    public Project? Project { get; init; }
    public Note? Note { get; init; }
    public string? GroupTitle { get; init; }
    public ObservableTreeChildren Children { get; } = new();

    public bool IsProject => Project is not null;
    public bool IsNote => Note is not null;
    public bool IsGroup => !string.IsNullOrWhiteSpace(GroupTitle);
    public string ItemId => Project?.Id ?? Note?.Id ?? string.Empty;
    public string DisplayTitle => Project?.Name ?? Note?.Title ?? GroupTitle ?? "Bez tytułu";
    public string TypeCode => Project?.ItemType.Code() ?? "N";
    public string TypeColor => Project?.ItemType.ColorHex() ?? "#64748B";
    public string TypeLabel => Project?.ItemType.Label() ?? (IsGroup ? "Grupa notatek" : "Notatka");
}

public sealed class ObservableTreeChildren : System.Collections.ObjectModel.ObservableCollection<NavigationTreeNode>
{
}
