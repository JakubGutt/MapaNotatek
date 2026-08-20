namespace MapaNotatek.Models;

public sealed class OpenTask
{
    public string Text { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceTitle { get; set; } = string.Empty;
    public bool IsProject { get; set; }
    public ChecklistItem Item { get; set; } = new();
}
