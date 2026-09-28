namespace MapaNotatek.Models;

/// <summary>
/// Describes the role of an item in the product architecture. Project and Folder
/// are retained for existing libraries and for work that does not belong to the
/// formal System → Product → Subsystem → Component hierarchy.
/// </summary>
public enum ProjectItemType
{
    Project,
    Folder,
    System,
    Product,
    Subsystem,
    Component
}

public static class ProjectItemTypeCatalog
{
    public static IReadOnlyList<ProjectItemType> ArchitectureTypes { get; } =
        [ProjectItemType.System, ProjectItemType.Product, ProjectItemType.Subsystem, ProjectItemType.Component];

    public static IReadOnlyList<ProjectItemType> CreatableTypes { get; } =
        [.. ArchitectureTypes, ProjectItemType.Project, ProjectItemType.Folder];

    public static string StorageValue(this ProjectItemType type) => type switch
    {
        ProjectItemType.Folder => "folder",
        ProjectItemType.System => "system",
        ProjectItemType.Product => "product",
        ProjectItemType.Subsystem => "subsystem",
        ProjectItemType.Component => "component",
        _ => "project"
    };

    public static ProjectItemType Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "folder" or "katalog" => ProjectItemType.Folder,
        "system" => ProjectItemType.System,
        "product" or "produkt" => ProjectItemType.Product,
        "subsystem" or "podsystem" => ProjectItemType.Subsystem,
        "component" or "komponent" => ProjectItemType.Component,
        _ => ProjectItemType.Project
    };

    public static string Label(this ProjectItemType type) => type switch
    {
        ProjectItemType.Folder => "Folder",
        ProjectItemType.System => "System",
        ProjectItemType.Product => "Produkt",
        ProjectItemType.Subsystem => "Podsystem",
        ProjectItemType.Component => "Komponent",
        _ => "Projekt ogólny"
    };

    public static string DefaultName(this ProjectItemType type) => type switch
    {
        ProjectItemType.Folder => "Nowy folder",
        ProjectItemType.System => "Nowy system",
        ProjectItemType.Product => "Nowy produkt",
        ProjectItemType.Subsystem => "Nowy podsystem",
        ProjectItemType.Component => "Nowy komponent",
        _ => "Nowy projekt"
    };

    public static string Code(this ProjectItemType type) => type switch
    {
        ProjectItemType.Folder => "F",
        ProjectItemType.System => "S",
        ProjectItemType.Product => "P",
        ProjectItemType.Subsystem => "PS",
        ProjectItemType.Component => "K",
        _ => "OG"
    };

    public static string ColorHex(this ProjectItemType type) => type switch
    {
        ProjectItemType.Folder => "#087F75",
        ProjectItemType.System => "#4338CA",
        ProjectItemType.Product => "#15803D",
        ProjectItemType.Subsystem => "#B45309",
        ProjectItemType.Component => "#BE185D",
        _ => "#285FBF"
    };

    public static string GraphClass(this ProjectItemType type) => type switch
    {
        ProjectItemType.Folder => "graph-folder",
        ProjectItemType.System => "graph-system",
        ProjectItemType.Product => "graph-product",
        ProjectItemType.Subsystem => "graph-subsystem",
        ProjectItemType.Component => "graph-component",
        _ => "graph-project"
    };

    public static string ContextDescription(this ProjectItemType type) => type switch
    {
        ProjectItemType.Folder => "Porządek dokumentów i elementów bez znaczenia architektonicznego",
        ProjectItemType.System => "Najwyższy poziom architektury, złożony z produktów",
        ProjectItemType.Product => "Samodzielnie sprzedawalna część systemu",
        ProjectItemType.Subsystem => "Grupa współpracujących komponentów produktu",
        ProjectItemType.Component => "Najmniejszy element architektury, np. PCB lub akumulator",
        _ => "Elastyczna przestrzeń pracy poza formalną architekturą"
    };

    public static string SearchText(this ProjectItemType type) => type switch
    {
        ProjectItemType.Folder => "folder katalog",
        ProjectItemType.System => "system",
        ProjectItemType.Product => "product produkt",
        ProjectItemType.Subsystem => "subsystem podsystem",
        ProjectItemType.Component => "component komponent",
        _ => "project projekt ogólny"
    };

    public static int SortOrder(this ProjectItemType type) => type switch
    {
        ProjectItemType.System => 0,
        ProjectItemType.Product => 1,
        ProjectItemType.Subsystem => 2,
        ProjectItemType.Component => 3,
        ProjectItemType.Project => 4,
        ProjectItemType.Folder => 5,
        _ => 9
    };

    public static bool CanContain(this ProjectItemType parent, ProjectItemType child) => parent switch
    {
        ProjectItemType.Folder or ProjectItemType.Project => true,
        ProjectItemType.System => child is ProjectItemType.Product or ProjectItemType.Folder,
        ProjectItemType.Product => child is ProjectItemType.Subsystem or ProjectItemType.Component or ProjectItemType.Folder,
        ProjectItemType.Subsystem => child is ProjectItemType.Component or ProjectItemType.Folder,
        _ => false
    };

    public static IReadOnlyList<ProjectItemType> AllowedChildren(this ProjectItemType parent) =>
        CreatableTypes.Where(child => parent.CanContain(child)).ToList();
}
