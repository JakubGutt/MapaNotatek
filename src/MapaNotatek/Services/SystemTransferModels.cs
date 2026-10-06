using System.ComponentModel;
using System.Text.Json.Serialization;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public sealed class SystemTransferManifest
{
    public string Format { get; set; } = "MapaNotatek-system-transfer";
    public int Version { get; set; } = 2;
    public Guid PackageId { get; set; }
    public Guid SourceLibraryId { get; set; }
    public string SourceSystemId { get; set; } = string.Empty;
    public string SourceSystemName { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<SystemTransferFileEntry> Files { get; set; } = [];
}

public sealed class SystemTransferFileEntry
{
    public string Path { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class SystemTransferSnapshot
{
    public List<TransferProject> Projects { get; set; } = [];
    public List<TransferNote> Notes { get; set; } = [];
    public List<TransferPerson> People { get; set; } = [];
    public Dictionary<string, TransferGraphPosition> Positions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TransferProject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> People { get; set; } = [];
    public List<ExternalLink> ExternalLinks { get; set; } = [];
    public List<string> SystemIds { get; set; } = [];
    public List<TransferChecklistItem> Checklist { get; set; } = [];
    public bool IsArchived { get; set; }
    public string? ParentId { get; set; }
    public ProjectItemType ItemType { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
}

public sealed class TransferNote
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> People { get; set; } = [];
    public List<ExternalLink> ExternalLinks { get; set; } = [];
    public List<string> RelatedNoteIds { get; set; } = [];
    public List<TransferChecklistItem> Checklist { get; set; } = [];
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
}

public sealed class TransferPerson
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AvatarPath { get; set; } = string.Empty;
    public List<ExternalLink> ExternalLinks { get; set; } = [];
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
}

public sealed class TransferChecklistItem
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public List<string> People { get; set; } = [];
    public int? Priority { get; set; }
    public List<ExternalLink> ExternalLinks { get; set; } = [];
}

public sealed class TransferGraphPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public enum MergeEntityKind
{
    Project,
    Note,
    Person
}

public enum MergeChangeType
{
    Added,
    Modified,
    Moved,
    Deleted
}

public enum MergeResolution
{
    Unresolved,
    Mine,
    Theirs,
    Both
}

public sealed class MergeConflict : INotifyPropertyChanged
{
    private MergeResolution _resolution;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Field { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string BaseText { get; init; } = string.Empty;
    public string MineText { get; init; } = string.Empty;
    public string TheirText { get; init; } = string.Empty;
    public bool CanKeepBoth { get; init; }

    [JsonIgnore]
    public MergeResolution Resolution
    {
        get => _resolution;
        set
        {
            if (_resolution == value)
            {
                return;
            }

            _resolution = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Resolution)));
            ResolutionChanged?.Invoke();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ResolutionChanged;
}

public sealed class MergeChange : INotifyPropertyChanged
{
    private bool _isSelected;

    public string SourceId { get; init; } = string.Empty;
    public string? LocalId { get; set; }
    public MergeEntityKind Kind { get; init; }
    public MergeChangeType ChangeType { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<MergeConflict> Conflicts { get; init; } = [];

    [JsonIgnore]
    public object? BaseEntity { get; init; }

    [JsonIgnore]
    public object? MineEntity { get; init; }

    [JsonIgnore]
    public object? TheirEntity { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            SelectionChanged?.Invoke();
        }
    }

    public bool HasUnresolvedConflicts =>
        IsSelected && Conflicts.Any(conflict => conflict.Resolution == MergeResolution.Unresolved);

    public string KindLabel => Kind switch
    {
        MergeEntityKind.Project => "Struktura",
        MergeEntityKind.Note => "Notatka",
        _ => "Osoba"
    };

    public string ChangeLabel => ChangeType switch
    {
        MergeChangeType.Added => "Dodano",
        MergeChangeType.Modified => "Zmieniono",
        MergeChangeType.Moved => "Przeniesiono",
        MergeChangeType.Deleted => "Usunięto",
        _ => ChangeType.ToString()
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? SelectionChanged;
}

public sealed class MergeProposal
{
    public required string PackagePath { get; init; }
    public required SystemTransferManifest Manifest { get; init; }
    public required SystemTransferSnapshot Incoming { get; init; }
    public ImportTrackingRecord? Tracking { get; init; }
    public bool IsFirstImport => Tracking is null;
    public List<MergeChange> Changes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public bool IsRevert { get; init; }
    public Guid? RevertsMergeId { get; init; }
    public bool HasUnresolvedConflicts => Changes.Any(change => change.HasUnresolvedConflicts);
    public int SelectedCount => Changes.Count(change => change.IsSelected);

    [JsonIgnore]
    internal ImportTrackingRecord WorkingTracking { get; init; } = new();

    [JsonIgnore]
    internal SystemTransferSnapshot IncomingLocal { get; init; } = new();

    [JsonIgnore]
    internal SystemTransferSnapshot CurrentLocal { get; init; } = new();
}

public sealed class ImportTrackingRecord
{
    public int Version { get; set; } = 1;
    public Guid SourceLibraryId { get; set; }
    public string SourceSystemId { get; set; } = string.Empty;
    public Guid LastPackageId { get; set; }
    public DateTimeOffset LastReviewedUtc { get; set; }
    public Dictionary<string, string> ProjectMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> NoteMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PersonMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public SystemTransferSnapshot BaseSource { get; set; } = new();
    public SystemTransferSnapshot BaseLocal { get; set; } = new();
    public List<MergeHistoryEntry> Merges { get; set; } = [];
}

public sealed class MergeHistoryEntry
{
    public Guid MergeId { get; set; }
    public Guid PackageId { get; set; }
    public DateTimeOffset MergedUtc { get; set; }
    public string SourceSystemName { get; set; } = string.Empty;
    public Guid SourceLibraryId { get; set; }
    public string SourceSystemId { get; set; } = string.Empty;
    public bool IsRevert { get; set; }
    public Guid? RevertsMergeId { get; set; }
    public int Added { get; set; }
    public int Modified { get; set; }
    public int Deleted { get; set; }
    public int Skipped { get; set; }
    public string BeforeSnapshotPath { get; set; } = string.Empty;
    public string AfterSnapshotPath { get; set; } = string.Empty;
    public string BeforeAssetsPath { get; set; } = string.Empty;
    public string AfterAssetsPath { get; set; } = string.Empty;
}

internal static class TransferModelCloner
{
    public static TransferChecklistItem From(ChecklistItem item) => new()
    {
        Id = item.Id,
        Text = item.Text,
        IsDone = item.IsDone,
        People = item.People.ToList(),
        Priority = item.Priority,
        ExternalLinks = ExternalLinkService.Clone(item.ExternalLinks)
    };

    public static ChecklistItem To(TransferChecklistItem item) => new()
    {
        Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
        Text = item.Text,
        IsDone = item.IsDone,
        People = item.People.ToList(),
        Priority = item.Priority,
        ExternalLinks = ExternalLinkService.Clone(item.ExternalLinks)
    };
}
