namespace MapaNotatek.Models;

public sealed class Person
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AvatarPath { get; set; } = string.Empty;
    public List<ExternalLink> ExternalLinks { get; set; } = [];
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
    public string FilePath { get; set; } = string.Empty;
    internal string PersistedContentHash { get; set; } = string.Empty;
}
