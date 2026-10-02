namespace MapaNotatek.Models;

public sealed class AppState
{
    public string? DataFolder { get; set; }
    public double Zoom { get; set; } = 1.0;
    public Dictionary<string, GraphPosition> NodePositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? FocusedProjectId { get; set; }
    public string EditorFont { get; set; } = "Inter";
    public double EditorFontSize { get; set; } = 16;
    public bool SidebarVisible { get; set; } = true;
    public bool EditorDetailsVisible { get; set; } = true;
    public string? BackupFolder { get; set; }
    public List<string> PinnedIds { get; set; } = [];
    public List<string> RecentIds { get; set; } = [];
}
