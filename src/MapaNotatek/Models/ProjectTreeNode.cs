namespace MapaNotatek.Models;

public sealed class ProjectTreeNode
{
    public Project Project { get; init; } = null!;
    public ObservableTreeChildren Children { get; } = new();

    public string DisplayTitle => Project.Name;
    public string TypeCode => Project.ItemType.Code();
    public string TypeColor => Project.ItemType.ColorHex();
    public string TypeLabel => Project.ItemType.Label();
}

public sealed class ObservableTreeChildren : System.Collections.ObjectModel.ObservableCollection<ProjectTreeNode>
{
}
