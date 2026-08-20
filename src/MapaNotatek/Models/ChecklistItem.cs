namespace MapaNotatek.Models;

public sealed class ChecklistItem
{
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
