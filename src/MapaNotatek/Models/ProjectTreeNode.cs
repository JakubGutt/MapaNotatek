namespace MapaNotatek.Models;

public sealed class ProjectTreeNode
{
    public Project Project { get; init; } = null!;
    public ObservableTreeChildren Children { get; } = new();

    public string DisplayTitle =>
        Project.IsFolder ? $"📁 {Project.Name}" : $"● {Project.Name}";
}

public sealed class ObservableTreeChildren : System.Collections.ObjectModel.ObservableCollection<ProjectTreeNode>
{
}
