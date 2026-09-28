namespace MapaNotatek.Models;

public enum DocumentBlockKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Bullet,
    Numbered,
    Checklist,
    Quote,
    Code,
    Rule,
    Image,
    Table
}

/// <summary>
/// Editable, in-memory representation of a Markdown document. RuntimeId is deliberately
/// not persisted: the canonical on-disk format remains portable Markdown.
/// </summary>
public sealed class DocumentBlock
{
    public string RuntimeId { get; } = Guid.NewGuid().ToString("N");
    public DocumentBlockKind Kind { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
    public List<string> People { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public List<List<string>> Cells { get; set; } = [];
}

public sealed record DocumentOutlineEntry(string BlockId, string Title, int Level);
