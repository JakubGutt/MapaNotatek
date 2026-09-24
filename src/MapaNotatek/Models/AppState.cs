namespace MapaNotatek.Models;

public sealed class AppState
{
    public string? DataFolder { get; set; }
    public double Zoom { get; set; } = 1.0;
    public Dictionary<string, GraphPosition> NodePositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? FocusedProjectId { get; set; }
    public List<string> PinnedIds { get; set; } = [];
    public List<string> RecentIds { get; set; } = [];
}
