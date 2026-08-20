namespace MapaNotatek.Models;

public sealed class AppState
{
    public string? DataFolder { get; set; }
    public double Zoom { get; set; } = 1.0;
    public Dictionary<string, GraphPosition> NodePositions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShowArchived { get; set; }
    public string? FocusedProjectId { get; set; }
}
