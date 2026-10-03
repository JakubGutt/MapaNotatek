using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class SystemMergeService
{
    private const string TrackingFileName = "tracking.json";
    private static readonly Regex TaskPeopleMetadata = new(
        @"<!--\s*people:\s*(?<people>[^>]*)-->",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static MergeProposal CreateProposal(string libraryRoot, string packagePath)
    {
        var root = SafeFileStorage.NormalizeDirectory(libraryRoot);
        var package = SystemTransferPackageService.Open(packagePath);
        var tracking = LoadTracking(root, package.Manifest);
        var working = tracking is null
            ? NewTracking(package.Manifest)
            : Clone(tracking);
        NormalizeMaps(working);

        var store = new MarkdownStore(root);
        var projects = store.LoadProjects();
        var notes = store.LoadNotes();
        var people = store.LoadPeople();
        AllocateMappings(package.Snapshot, working, projects, notes, people);
        var incomingLocal = MapIncoming(package.Snapshot, working, projects, people);
        var currentLocal = CaptureMappedLocal(projects, notes, people, working);
        var changes = BuildChanges(
            tracking?.BaseSource ?? new SystemTransferSnapshot(),
            tracking?.BaseLocal ?? new SystemTransferSnapshot(),
            package.Snapshot,
            incomingLocal,
            currentLocal,
            working);
        var warnings = package.Manifest.Warnings.ToList();
        var localManifest = LibrarySchemaService.ReadManifest(Path.Combine(root, LibrarySchemaService.ManifestFileName));
        if (localManifest.LibraryId == package.Manifest.SourceLibraryId && tracking is null)
        {
            warnings.Add("Pakiet pochodzi z biblioteki o tym samym identyfikatorze. Zgodnie z zasadą pierwszego scalenia zostanie dodany jako osobny system.");
        }

        if (tracking?.LastPackageId == package.Manifest.PackageId)
        {
            warnings.Add("Ta paczka została już wcześniej przejrzana.");
        }

        var proposal = new MergeProposal
        {
            PackagePath = package.Path,
            Manifest = package.Manifest,
            Incoming = package.Snapshot,
            Tracking = tracking,
            WorkingTracking = working,
            IncomingLocal = incomingLocal,
            CurrentLocal = currentLocal,
            Changes = changes,
            Warnings = warnings
        };
        WireProposal(proposal);
        return proposal;
    }

    public static IReadOnlyList<string> ValidateSelections(MergeProposal proposal)
    {
        var errors = new List<string>();
        if (proposal.HasUnresolvedConflicts)
        {
            errors.Add("Rozwiąż wszystkie konflikty w zaznaczonych zmianach.");
        }

        var selected = proposal.Changes.Where(change => change.IsSelected)
            .ToDictionary(change => (change.Kind, change.SourceId));
        var activeProjectSources = ActiveSources(
            proposal.WorkingTracking.ProjectMap,
            proposal.CurrentLocal.Projects.Select(project => project.Id),
            proposal.Changes,
            MergeEntityKind.Project);
        var activeNoteSources = ActiveSources(
            proposal.WorkingTracking.NoteMap,
            proposal.CurrentLocal.Notes.Select(note => note.Id),
            proposal.Changes,
            MergeEntityKind.Note);
        var activePersonSources = ActiveSources(
            proposal.WorkingTracking.PersonMap,
            proposal.CurrentLocal.People.Select(person => person.Id),
            proposal.Changes,
            MergeEntityKind.Person);

        foreach (var change in proposal.Changes.Where(change => change.IsSelected && change.ChangeType != MergeChangeType.Deleted))
        {
            switch (change.TheirEntity)
            {
                case TransferProject project:
                    Require(SourceForLocal(proposal.WorkingTracking.ProjectMap, project.ParentId), activeProjectSources, selected, MergeEntityKind.Project,
                        $"„{change.Title}” wymaga zaakceptowania albo wskazania rodzica.");
                    foreach (var id in project.SystemIds)
                    {
                        Require(SourceForLocal(proposal.WorkingTracking.ProjectMap, id), activeProjectSources, selected, MergeEntityKind.Project,
                            $"„{change.Title}” odwołuje się do odrzuconego systemu.");
                    }
                    foreach (var slug in project.People)
                    {
                        Require(PersonSourceId(proposal.IncomingLocal, proposal.WorkingTracking, slug), activePersonSources, selected,
                            MergeEntityKind.Person, $"„{change.Title}” odwołuje się do odrzuconej osoby.");
                    }
                    foreach (var slug in project.Checklist.SelectMany(task => task.People))
                    {
                        Require(PersonSourceId(proposal.IncomingLocal, proposal.WorkingTracking, slug), activePersonSources, selected,
                            MergeEntityKind.Person, $"Task w „{change.Title}” odwołuje się do odrzuconej osoby.");
                    }
                    break;
                case TransferNote note:
                    foreach (var tag in note.Tags)
                    {
                        Require(ProjectSourceId(proposal.IncomingLocal, proposal.WorkingTracking, tag), activeProjectSources, selected,
                            MergeEntityKind.Project, $"„{change.Title}” odwołuje się do odrzuconego projektu.");
                    }
                    foreach (var id in note.RelatedNoteIds)
                    {
                        Require(SourceForLocal(proposal.WorkingTracking.NoteMap, id), activeNoteSources, selected, MergeEntityKind.Note,
                            $"„{change.Title}” odwołuje się do odrzuconej notatki.");
                    }
                    foreach (var slug in note.People)
                    {
                        Require(PersonSourceId(proposal.IncomingLocal, proposal.WorkingTracking, slug), activePersonSources, selected,
                            MergeEntityKind.Person, $"„{change.Title}” odwołuje się do odrzuconej osoby.");
                    }
                    foreach (var slug in note.Checklist.SelectMany(task => task.People))
                    {
                        Require(PersonSourceId(proposal.IncomingLocal, proposal.WorkingTracking, slug), activePersonSources, selected,
                            MergeEntityKind.Person, $"Task w „{change.Title}” odwołuje się do odrzuconej osoby.");
                    }
                    break;
            }
        }

        if (!proposal.HasUnresolvedConflicts)
        {
            ValidateDeletionDependencies(proposal, errors);
        }

        return errors.Distinct(StringComparer.CurrentCulture).ToList();

        void Require(
            string? sourceId,
            HashSet<string> active,
            IReadOnlyDictionary<(MergeEntityKind, string), MergeChange> selectedChanges,
            MergeEntityKind kind,
            string message)
        {
            if (string.IsNullOrWhiteSpace(sourceId) || active.Contains(sourceId))
            {
                return;
            }

            if (selectedChanges.TryGetValue((kind, sourceId), out var required) && required.ChangeType != MergeChangeType.Deleted)
            {
                active.Add(sourceId);
                return;
            }

            errors.Add(message);
        }
    }

    private static void ValidateDeletionDependencies(MergeProposal proposal, ICollection<string> errors)
    {
        var projects = proposal.CurrentLocal.Projects.ToDictionary(item => item.Id, Clone, StringComparer.OrdinalIgnoreCase);
        var notes = proposal.CurrentLocal.Notes.ToDictionary(item => item.Id, Clone, StringComparer.OrdinalIgnoreCase);
        var people = proposal.CurrentLocal.People.ToDictionary(item => item.Id, Clone, StringComparer.OrdinalIgnoreCase);
        var removedProjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removedProjectSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removedNoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removedPersonSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in proposal.Changes.Where(change => change.IsSelected))
        {
            if (change.ChangeType == MergeChangeType.Deleted && ShouldDelete(change))
            {
                switch (change.MineEntity)
                {
                    case TransferProject project:
                        projects.Remove(project.Id);
                        removedProjectIds.Add(project.Id);
                        removedProjectSlugs.Add(project.Slug);
                        break;
                    case TransferNote note:
                        notes.Remove(note.Id);
                        removedNoteIds.Add(note.Id);
                        break;
                    case TransferPerson person:
                        people.Remove(person.Id);
                        removedPersonSlugs.Add(person.Slug);
                        break;
                }
                continue;
            }

            if (change.ChangeType == MergeChangeType.Deleted || !ShouldApplyIncoming(change))
            {
                continue;
            }

            switch (change.TheirEntity)
            {
                case TransferProject incoming:
                    projects.TryGetValue(incoming.Id, out var currentProject);
                    projects[incoming.Id] = MergeProject(change, currentProject, incoming);
                    break;
                case TransferNote incoming:
                    notes.TryGetValue(incoming.Id, out var currentNote);
                    notes[incoming.Id] = MergeNote(change, currentNote, incoming);
                    break;
                case TransferPerson incoming:
                    people.TryGetValue(incoming.Id, out var currentPerson);
                    people[incoming.Id] = MergePerson(change, currentPerson, incoming);
                    break;
            }
        }

        foreach (var project in projects.Values)
        {
            if (project.ParentId is not null && removedProjectIds.Contains(project.ParentId))
            {
                errors.Add($"„{project.Name}” nadal wskazuje usuwany element nadrzędny.");
            }
            if (project.SystemIds.Any(removedProjectIds.Contains))
            {
                errors.Add($"„{project.Name}” nadal należy do usuwanego systemu.");
            }
            if (project.People.Concat(project.Checklist.SelectMany(task => task.People)).Any(removedPersonSlugs.Contains))
            {
                errors.Add($"„{project.Name}” albo jego task nadal wskazuje usuwaną osobę.");
            }
        }

        foreach (var note in notes.Values)
        {
            if (note.Tags.Any(removedProjectSlugs.Contains))
            {
                errors.Add($"„{note.Title}” nadal wskazuje usuwany projekt.");
            }
            if (note.RelatedNoteIds.Any(removedNoteIds.Contains))
            {
                errors.Add($"„{note.Title}” nadal wskazuje usuwaną notatkę.");
            }
            if (note.People.Concat(note.Checklist.SelectMany(task => task.People)).Any(removedPersonSlugs.Contains))
            {
                errors.Add($"„{note.Title}” albo jego task nadal wskazuje usuwaną osobę.");
            }
        }
    }

    public static MergeApplyResult Apply(string libraryRoot, MergeProposal proposal)
    {
        var selectionErrors = ValidateSelections(proposal);
        if (selectionErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, selectionErrors));
        }

        var currentPackage = SystemTransferPackageService.Open(proposal.PackagePath);
        if (currentPackage.Manifest.PackageId != proposal.Manifest.PackageId)
        {
            throw new InvalidDataException("Pakiet zmienił się po otwarciu podglądu. Otwórz propozycję ponownie.");
        }

        var root = SafeFileStorage.NormalizeDirectory(libraryRoot);
        RecoverInterruptedMerge(root);
        var mergeId = Guid.NewGuid();
        var transaction = PrepareTransaction(root, mergeId);
        try
        {
            ApplyToStaging(transaction.Stage, root, proposal, currentPackage, mergeId);
            ValidateStaging(transaction.Stage);
            CommitTransaction(transaction);
            return new MergeApplyResult(
                mergeId,
                proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Added),
                proposal.Changes.Count(change => change.IsSelected && change.ChangeType is MergeChangeType.Modified or MergeChangeType.Moved),
                proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Deleted),
                proposal.Changes.Count(change => !change.IsSelected));
        }
        catch
        {
            RollbackTransaction(transaction);
            throw;
        }
    }

    public static MergeProposal CreateRevertProposal(string libraryRoot, Guid mergeId)
    {
        var root = SafeFileStorage.NormalizeDirectory(libraryRoot);
        var context = FindHistory(root, mergeId);
        var before = ReadSnapshot(context.TrackingFolder, context.Entry.BeforeSnapshotPath);
        var after = ReadSnapshot(context.TrackingFolder, context.Entry.AfterSnapshotPath);
        var store = new MarkdownStore(root);
        var current = CaptureMappedLocal(
            store.LoadProjects(), store.LoadNotes(), store.LoadPeople(), context.Tracking);
        var identity = IdentityTracking(context.Tracking, before, after, current);
        var changes = BuildChanges(after, after, before, before, current, identity);
        var manifest = new SystemTransferManifest
        {
            PackageId = Guid.NewGuid(),
            SourceLibraryId = context.Tracking.SourceLibraryId,
            SourceSystemId = context.Tracking.SourceSystemId,
            SourceSystemName = "Cofnięcie: " + context.Entry.SourceSystemName,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        var proposal = new MergeProposal
        {
            PackagePath = string.Empty,
            Manifest = manifest,
            Incoming = before,
            Tracking = context.Tracking,
            WorkingTracking = identity,
            IncomingLocal = before,
            CurrentLocal = current,
            Changes = changes,
            IsRevert = true,
            RevertsMergeId = mergeId,
            Warnings =
            [
                "To jest odwrotna propozycja zmian. Późniejsze lokalne edycje pozostają w mainie i mogą utworzyć konflikty."
            ]
        };
        WireProposal(proposal);
        return proposal;
    }

    public static MergeApplyResult ApplyRevert(string libraryRoot, MergeProposal proposal)
    {
        if (!proposal.IsRevert || proposal.RevertsMergeId is null)
        {
            throw new InvalidOperationException("Ta propozycja nie jest cofnięciem scalenia.");
        }

        var errors = ValidateSelections(proposal);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        var root = SafeFileStorage.NormalizeDirectory(libraryRoot);
        var context = FindHistory(root, proposal.RevertsMergeId.Value);
        RecoverInterruptedMerge(root);
        var mergeId = Guid.NewGuid();
        var transaction = PrepareTransaction(root, mergeId);
        try
        {
            ApplyRevertToStaging(transaction.Stage, proposal, context, mergeId);
            ValidateStaging(transaction.Stage);
            CommitTransaction(transaction);
            return new MergeApplyResult(
                mergeId,
                proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Added),
                proposal.Changes.Count(change => change.IsSelected && change.ChangeType is MergeChangeType.Modified or MergeChangeType.Moved),
                proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Deleted),
                proposal.Changes.Count(change => !change.IsSelected));
        }
        catch
        {
            RollbackTransaction(transaction);
            throw;
        }
    }

    public static IReadOnlyList<MergeHistoryEntry> ListHistory(string libraryRoot)
    {
        var imports = Path.Combine(SafeFileStorage.NormalizeDirectory(libraryRoot), "Imports");
        if (!Directory.Exists(imports))
        {
            return [];
        }

        var result = new List<MergeHistoryEntry>();
        foreach (var path in Directory.EnumerateFiles(imports, TrackingFileName, SearchOption.AllDirectories))
        {
            try
            {
                var tracking = JsonSerializer.Deserialize<ImportTrackingRecord>(
                    File.ReadAllText(path), SystemTransferPackageService.JsonOptions);
                if (tracking is not null)
                {
                    result.AddRange(tracking.Merges);
                }
            }
            catch
            {
                // A damaged history entry must not prevent the library from opening.
            }
        }

        return result.OrderByDescending(item => item.MergedUtc).ToList();
    }

    public static void RecoverInterruptedMerge(string libraryRoot)
    {
        var root = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var journalPath = JournalPath(root);
        if (!File.Exists(journalPath))
        {
            return;
        }

        MergeTransaction? transaction;
        try
        {
            transaction = JsonSerializer.Deserialize<MergeTransaction>(File.ReadAllText(journalPath),
                SystemTransferPackageService.JsonOptions);
        }
        catch
        {
            return;
        }

        if (transaction is null || !string.Equals(Path.GetFullPath(transaction.Root), root, PathComparison))
        {
            return;
        }

        if (!Directory.Exists(root) && Directory.Exists(transaction.Rollback))
        {
            Directory.Move(transaction.Rollback, root);
        }
        else if (Directory.Exists(root) && Directory.Exists(transaction.Rollback))
        {
            var keepCommitted = false;
            if (transaction.Phase == MergeTransactionPhase.Committed)
            {
                try
                {
                    ValidateStaging(root);
                    keepCommitted = true;
                }
                catch
                {
                    keepCommitted = false;
                }
            }

            if (!keepCommitted)
            {
                var failed = transaction.Stage + ".failed";
                if (!Directory.Exists(failed))
                {
                    Directory.Move(root, failed);
                }
                Directory.Move(transaction.Rollback, root);
            }
        }

        if (Directory.Exists(root))
        {
            TryDeleteDirectory(transaction.Stage);
            TryDeleteDirectory(transaction.Backup);
            TryDeleteDirectory(transaction.Rollback);
            TryDeleteFile(journalPath);
        }
    }

    private static List<MergeChange> BuildChanges(
        SystemTransferSnapshot baseSource,
        SystemTransferSnapshot baseLocal,
        SystemTransferSnapshot incomingSource,
        SystemTransferSnapshot incomingLocal,
        SystemTransferSnapshot currentLocal,
        ImportTrackingRecord tracking)
    {
        var changes = new List<MergeChange>();
        BuildKind(
            MergeEntityKind.Project,
            baseSource.Projects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            incomingSource.Projects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            baseLocal.Projects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            incomingLocal.Projects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            currentLocal.Projects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            tracking.ProjectMap,
            ProjectTitle,
            BuildProjectConflicts,
            changes);
        BuildKind(
            MergeEntityKind.Note,
            baseSource.Notes.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            incomingSource.Notes.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            baseLocal.Notes.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            incomingLocal.Notes.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            currentLocal.Notes.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            tracking.NoteMap,
            NoteTitle,
            BuildNoteConflicts,
            changes);
        BuildKind(
            MergeEntityKind.Person,
            baseSource.People.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            incomingSource.People.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            baseLocal.People.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            incomingLocal.People.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            currentLocal.People.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase),
            tracking.PersonMap,
            PersonTitle,
            BuildPersonConflicts,
            changes);
        return changes.OrderBy(change => change.ChangeType).ThenBy(change => change.Kind)
            .ThenBy(change => change.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void BuildKind<T>(
        MergeEntityKind kind,
        IReadOnlyDictionary<string, T> baseSource,
        IReadOnlyDictionary<string, T> incomingSource,
        IReadOnlyDictionary<string, T> baseLocal,
        IReadOnlyDictionary<string, T> incomingLocal,
        IReadOnlyDictionary<string, T> currentLocal,
        IReadOnlyDictionary<string, string> mapping,
        Func<T, string> title,
        Func<string, T, T, T, List<MergeConflict>> conflictBuilder,
        ICollection<MergeChange> changes)
        where T : class
    {
        var sourceIds = baseSource.Keys.Concat(incomingSource.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceId in sourceIds)
        {
            baseSource.TryGetValue(sourceId, out var sourceBase);
            incomingSource.TryGetValue(sourceId, out var sourceIncoming);
            mapping.TryGetValue(sourceId, out var localId);
            var localBase = localId is null ? null : baseLocal.GetValueOrDefault(localId);
            var localIncoming = localId is null ? null : incomingLocal.GetValueOrDefault(localId);
            var mine = localId is null ? null : currentLocal.GetValueOrDefault(localId);

            if (sourceBase is null && sourceIncoming is not null && localIncoming is not null)
            {
                changes.Add(NewChange(kind, sourceId, localId, MergeChangeType.Added, title(sourceIncoming),
                    "Nowy element z biblioteki kolegi.", null, mine, localIncoming, [], selected: true));
                continue;
            }

            if (sourceBase is not null && sourceIncoming is null)
            {
                if (mine is null)
                {
                    continue;
                }

                var deletionConflicts = new List<MergeConflict>();
                if (localBase is not null && !SemanticEquals(localBase, mine))
                {
                    deletionConflicts.Add(new MergeConflict
                    {
                        Id = sourceId + ":deleted",
                        Field = "deleted",
                        Label = "Usunięcie kontra lokalna edycja",
                        BaseText = title(localBase),
                        MineText = title(mine) + " (zmieniono lokalnie)",
                        TheirText = "(usunięty po stronie kolegi)",
                        CanKeepBoth = false
                    });
                }
                changes.Add(NewChange(kind, sourceId, localId, MergeChangeType.Deleted, title(mine),
                    deletionConflicts.Count == 0
                        ? "Element został usunięty po stronie kolegi."
                        : "Usunięcie koliduje z późniejszą lokalną zmianą.",
                    localBase, mine, null, deletionConflicts, selected: false));
                continue;
            }

            if (sourceBase is null || sourceIncoming is null || localIncoming is null)
            {
                continue;
            }

            if (SemanticEquals(sourceBase, sourceIncoming))
            {
                continue;
            }

            if (mine is null)
            {
                var conflict = new MergeConflict
                {
                    Id = sourceId + ":locally-deleted",
                    Field = "deleted",
                    Label = "Element usunięty lokalnie",
                    BaseText = title(sourceBase),
                    MineText = "(usunięty lokalnie)",
                    TheirText = title(sourceIncoming),
                    CanKeepBoth = false
                };
                changes.Add(NewChange(kind, sourceId, localId, MergeChangeType.Modified, title(sourceIncoming),
                    "Kolega zmienił element usunięty lokalnie.", localBase, null, localIncoming, [conflict], selected: true));
                continue;
            }

            var mineChanged = localBase is null || !SemanticEquals(localBase, mine);
            var conflicts = mineChanged && localBase is not null
                ? conflictBuilder(sourceId, localBase, mine, localIncoming)
                : [];
            var changeType = kind == MergeEntityKind.Project && sourceBase is TransferProject baseProject &&
                             sourceIncoming is TransferProject incomingProject &&
                             !string.Equals(baseProject.ParentId, incomingProject.ParentId, StringComparison.OrdinalIgnoreCase)
                ? MergeChangeType.Moved
                : MergeChangeType.Modified;
            changes.Add(NewChange(kind, sourceId, localId, changeType, title(sourceIncoming),
                conflicts.Count == 0
                    ? "Zmiana może zostać scalona automatycznie."
                    : $"Wymaga decyzji w {conflicts.Count} miejscu/miejscach.",
                localBase, mine, localIncoming, conflicts, selected: true));
        }
    }

    private static MergeChange NewChange(
        MergeEntityKind kind,
        string sourceId,
        string? localId,
        MergeChangeType changeType,
        string title,
        string summary,
        object? baseEntity,
        object? mineEntity,
        object? theirEntity,
        List<MergeConflict> conflicts,
        bool selected)
    {
        var change = new MergeChange
        {
            Kind = kind,
            SourceId = sourceId,
            LocalId = localId,
            ChangeType = changeType,
            Title = title,
            Summary = summary,
            BaseEntity = baseEntity,
            MineEntity = mineEntity,
            TheirEntity = theirEntity,
            Conflicts = conflicts,
            IsSelected = selected
        };
        return change;
    }

    private static List<MergeConflict> BuildProjectConflicts(
        string sourceId,
        TransferProject baseline,
        TransferProject mine,
        TransferProject theirs)
    {
        var result = new List<MergeConflict>();
        AddScalarConflict(result, sourceId, "name", "Nazwa", baseline.Name, mine.Name, theirs.Name);
        AddScalarConflict(result, sourceId, "type", "Typ elementu", baseline.ItemType, mine.ItemType, theirs.ItemType);
        AddScalarConflict(result, sourceId, "parent", "Miejsce w drzewie", baseline.ParentId, mine.ParentId, theirs.ParentId);
        AddScalarConflict(result, sourceId, "archived", "Archiwizacja", baseline.IsArchived, mine.IsArchived, theirs.IsArchived);
        AddListConflict(result, sourceId, "people", "Osoby", baseline.People, mine.People, theirs.People);
        AddListConflict(result, sourceId, "systems", "Przynależność do systemów", baseline.SystemIds, mine.SystemIds, theirs.SystemIds);
        result.AddRange(ThreeWayTextMerge.Merge(
            baseline.Description, mine.Description, theirs.Description,
            sourceId + ":description", "Treść projektu / taski").Conflicts);
        return result;
    }

    private static List<MergeConflict> BuildNoteConflicts(
        string sourceId,
        TransferNote baseline,
        TransferNote mine,
        TransferNote theirs)
    {
        var result = new List<MergeConflict>();
        AddScalarConflict(result, sourceId, "title", "Tytuł", baseline.Title, mine.Title, theirs.Title);
        AddListConflict(result, sourceId, "tags", "Projekty i tagi", baseline.Tags, mine.Tags, theirs.Tags);
        AddListConflict(result, sourceId, "people", "Osoby", baseline.People, mine.People, theirs.People);
        AddListConflict(result, sourceId, "relations", "Powiązane notatki", baseline.RelatedNoteIds, mine.RelatedNoteIds, theirs.RelatedNoteIds);
        result.AddRange(ThreeWayTextMerge.Merge(
            baseline.Body, mine.Body, theirs.Body,
            sourceId + ":body", "Treść notatki / taski").Conflicts);
        return result;
    }

    private static List<MergeConflict> BuildPersonConflicts(
        string sourceId,
        TransferPerson baseline,
        TransferPerson mine,
        TransferPerson theirs)
    {
        var result = new List<MergeConflict>();
        AddScalarConflict(result, sourceId, "name", "Nazwa osoby", baseline.Name, mine.Name, theirs.Name);
        AddScalarConflict(result, sourceId, "role", "Rola", baseline.Role, mine.Role, theirs.Role);
        AddScalarConflict(result, sourceId, "avatar", "Zdjęcie", baseline.AvatarPath, mine.AvatarPath, theirs.AvatarPath);
        result.AddRange(ThreeWayTextMerge.Merge(
            baseline.Description, mine.Description, theirs.Description,
            sourceId + ":description", "Opis osoby").Conflicts);
        return result;
    }

    private static void AddScalarConflict<T>(
        ICollection<MergeConflict> result,
        string sourceId,
        string field,
        string label,
        T baseline,
        T mine,
        T theirs)
    {
        if (Equals(mine, baseline) || Equals(theirs, baseline) || Equals(mine, theirs))
        {
            return;
        }

        result.Add(new MergeConflict
        {
            Id = $"{sourceId}:{field}",
            Field = field,
            Label = label,
            BaseText = Display(baseline),
            MineText = Display(mine),
            TheirText = Display(theirs),
            CanKeepBoth = false
        });
    }

    private static void AddListConflict(
        ICollection<MergeConflict> result,
        string sourceId,
        string field,
        string label,
        IReadOnlyCollection<string> baseline,
        IReadOnlyCollection<string> mine,
        IReadOnlyCollection<string> theirs)
    {
        if (SetEquals(mine, baseline) || SetEquals(theirs, baseline) || SetEquals(mine, theirs))
        {
            return;
        }

        result.Add(new MergeConflict
        {
            Id = $"{sourceId}:{field}",
            Field = field,
            Label = label,
            BaseText = string.Join(", ", baseline),
            MineText = string.Join(", ", mine),
            TheirText = string.Join(", ", theirs),
            CanKeepBoth = true
        });
    }

    private static void ApplyToStaging(
        string stageRoot,
        string finalRoot,
        MergeProposal proposal,
        LoadedSystemTransferPackage package,
        Guid mergeId)
    {
        var store = new MarkdownStore(stageRoot);
        var projects = store.LoadProjects();
        var notes = store.LoadNotes();
        var people = store.LoadPeople();
        var tracking = Clone(proposal.WorkingTracking);
        NormalizeMaps(tracking);
        var before = CaptureMappedLocal(projects, notes, people, tracking);
        var trackingFolder = TrackingFolder(stageRoot, package.Manifest);
        Directory.CreateDirectory(trackingFolder);
        var mergesFolder = Path.Combine(trackingFolder, "Merges");
        Directory.CreateDirectory(mergesFolder);
        var affectedLocalIds = proposal.Changes.Where(change => change.IsSelected && change.LocalId is not null)
            .Select(change => change.LocalId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var beforeAssetsName = $"{mergeId:N}-before-assets";
        var afterAssetsName = $"{mergeId:N}-after-assets";
        ArchiveAssetFolders(stageRoot, affectedLocalIds, Path.Combine(mergesFolder, beforeAssetsName));

        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.ChangeType == MergeChangeType.Deleted &&
                                                                 ShouldDelete(change)))
        {
            switch (change.Kind)
            {
                case MergeEntityKind.Note:
                    if (notes.FirstOrDefault(item => item.Id == change.LocalId) is { } note)
                    {
                        store.MoveNoteToTrash(note);
                        notes.Remove(note);
                    }
                    break;
                case MergeEntityKind.Project:
                    if (projects.FirstOrDefault(item => item.Id == change.LocalId) is { } project)
                    {
                        store.MoveProjectToTrash(project);
                        projects.Remove(project);
                    }
                    break;
                case MergeEntityKind.Person:
                    if (people.FirstOrDefault(item => item.Id == change.LocalId) is { } person)
                    {
                        store.MovePersonToTrash(person);
                        people.Remove(person);
                    }
                    break;
            }
        }

        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.Kind == MergeEntityKind.Person &&
                                                                 change.ChangeType != MergeChangeType.Deleted &&
                                                                 ShouldApplyIncoming(change)))
        {
            var incoming = (TransferPerson)change.TheirEntity!;
            var current = people.FirstOrDefault(item => item.Id == change.LocalId);
            var merged = MergePerson(change, current is null ? null : FromPerson(current), incoming);
            if (current is null)
            {
                current = ToPerson(merged);
                people.Add(current);
            }
            else
            {
                Apply(merged, current);
            }
            store.SavePerson(current);
            CopyAssets(package, change.SourceId, current.Id, stageRoot);
        }

        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.Kind == MergeEntityKind.Project &&
                                                                 change.ChangeType != MergeChangeType.Deleted &&
                                                                 ShouldApplyIncoming(change)))
        {
            var incoming = (TransferProject)change.TheirEntity!;
            var current = projects.FirstOrDefault(item => item.Id == change.LocalId);
            var merged = MergeProject(change, current is null ? null : FromProject(current), incoming);
            if (current is null)
            {
                current = ToProject(merged);
                projects.Add(current);
            }
            else
            {
                Apply(merged, current);
            }
            store.SaveProject(current);
            CopyAssets(package, change.SourceId, current.Id, stageRoot);
        }

        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.Kind == MergeEntityKind.Note &&
                                                                 change.ChangeType != MergeChangeType.Deleted &&
                                                                 ShouldApplyIncoming(change)))
        {
            var incoming = (TransferNote)change.TheirEntity!;
            var current = notes.FirstOrDefault(item => item.Id == change.LocalId);
            var merged = MergeNote(change, current is null ? null : FromNote(current), incoming);
            if (current is null)
            {
                current = ToNote(merged);
                notes.Add(current);
            }
            else
            {
                Apply(merged, current);
            }
            store.SaveNote(current);
            CopyAssets(package, change.SourceId, current.Id, stageRoot);
        }

        UpdateGraphPositions(stageRoot, finalRoot, proposal, projects, notes);
        var after = CaptureMappedLocal(projects, notes, people, tracking);
        ArchiveAssetFolders(stageRoot, affectedLocalIds, Path.Combine(mergesFolder, afterAssetsName));
        PruneMappings(tracking, after);
        tracking.BaseSource = Clone(package.Snapshot);
        tracking.BaseLocal = after;
        tracking.LastPackageId = package.Manifest.PackageId;
        tracking.LastReviewedUtc = DateTimeOffset.UtcNow;

        var beforeName = $"{mergeId:N}-before.json";
        var afterName = $"{mergeId:N}-after.json";
        WriteJsonDurably(Path.Combine(mergesFolder, beforeName), before);
        WriteJsonDurably(Path.Combine(mergesFolder, afterName), after);
        tracking.Merges.Add(new MergeHistoryEntry
        {
            MergeId = mergeId,
            PackageId = package.Manifest.PackageId,
            MergedUtc = DateTimeOffset.UtcNow,
            SourceSystemName = package.Manifest.SourceSystemName,
            SourceLibraryId = package.Manifest.SourceLibraryId,
            SourceSystemId = package.Manifest.SourceSystemId,
            Added = proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Added),
            Modified = proposal.Changes.Count(change => change.IsSelected && change.ChangeType is MergeChangeType.Modified or MergeChangeType.Moved),
            Deleted = proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Deleted),
            Skipped = proposal.Changes.Count(change => !change.IsSelected),
            BeforeSnapshotPath = "Merges/" + beforeName,
            AfterSnapshotPath = "Merges/" + afterName,
            BeforeAssetsPath = "Merges/" + beforeAssetsName,
            AfterAssetsPath = "Merges/" + afterAssetsName
        });
        WriteJsonDurably(Path.Combine(trackingFolder, TrackingFileName), tracking);
    }

    private static void ApplyRevertToStaging(
        string stageRoot,
        MergeProposal proposal,
        MergeHistoryContext originalContext,
        Guid mergeId)
    {
        var manifest = new SystemTransferManifest
        {
            SourceLibraryId = originalContext.Tracking.SourceLibraryId,
            SourceSystemId = originalContext.Tracking.SourceSystemId
        };
        var trackingFolder = TrackingFolder(stageRoot, manifest);
        var trackingPath = Path.Combine(trackingFolder, TrackingFileName);
        var tracking = JsonSerializer.Deserialize<ImportTrackingRecord>(
            File.ReadAllText(trackingPath), SystemTransferPackageService.JsonOptions)
            ?? throw new InvalidDataException("Brakuje historii potrzebnej do cofnięcia scalenia.");
        NormalizeMaps(tracking);

        var store = new MarkdownStore(stageRoot);
        var projects = store.LoadProjects();
        var notes = store.LoadNotes();
        var people = store.LoadPeople();
        var before = CaptureMappedLocal(projects, notes, people, tracking);
        var mergesFolder = Path.Combine(trackingFolder, "Merges");
        Directory.CreateDirectory(mergesFolder);
        var affectedIds = proposal.Changes.Where(change => change.IsSelected && change.LocalId is not null)
            .Select(change => change.LocalId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var beforeName = $"{mergeId:N}-before.json";
        var afterName = $"{mergeId:N}-after.json";
        var beforeAssetsName = $"{mergeId:N}-before-assets";
        var afterAssetsName = $"{mergeId:N}-after-assets";
        ArchiveAssetFolders(stageRoot, affectedIds, Path.Combine(mergesFolder, beforeAssetsName));

        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.ChangeType == MergeChangeType.Deleted &&
                                                                 ShouldDelete(change)))
        {
            switch (change.Kind)
            {
                case MergeEntityKind.Note:
                    if (notes.FirstOrDefault(item => item.Id == change.LocalId) is { } note)
                    {
                        store.MoveNoteToTrash(note);
                        notes.Remove(note);
                    }
                    break;
                case MergeEntityKind.Project:
                    if (projects.FirstOrDefault(item => item.Id == change.LocalId) is { } project)
                    {
                        store.MoveProjectToTrash(project);
                        projects.Remove(project);
                    }
                    break;
                case MergeEntityKind.Person:
                    if (people.FirstOrDefault(item => item.Id == change.LocalId) is { } person)
                    {
                        store.MovePersonToTrash(person);
                        people.Remove(person);
                    }
                    break;
            }
        }

        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.Kind == MergeEntityKind.Person &&
                                                                 change.ChangeType != MergeChangeType.Deleted &&
                                                                 ShouldApplyIncoming(change)))
        {
            var incoming = (TransferPerson)change.TheirEntity!;
            var current = people.FirstOrDefault(item => item.Id == change.LocalId);
            var merged = MergePerson(change, current is null ? null : FromPerson(current), incoming);
            if (current is null)
            {
                merged.Slug = SlugHelper.Unique(merged.Slug, people.Select(item => item.Slug));
                current = ToPerson(merged);
                people.Add(current);
            }
            else Apply(merged, current);
            store.SavePerson(current);
        }

        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.Kind == MergeEntityKind.Project &&
                                                                 change.ChangeType != MergeChangeType.Deleted &&
                                                                 ShouldApplyIncoming(change)))
        {
            var incoming = (TransferProject)change.TheirEntity!;
            var current = projects.FirstOrDefault(item => item.Id == change.LocalId);
            var merged = MergeProject(change, current is null ? null : FromProject(current), incoming);
            if (current is null)
            {
                merged.Slug = SlugHelper.Unique(merged.Slug, projects.Select(item => item.Slug));
                current = ToProject(merged);
                projects.Add(current);
            }
            else Apply(merged, current);
            store.SaveProject(current);
        }

        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.Kind == MergeEntityKind.Note &&
                                                                 change.ChangeType != MergeChangeType.Deleted &&
                                                                 ShouldApplyIncoming(change)))
        {
            var incoming = (TransferNote)change.TheirEntity!;
            var current = notes.FirstOrDefault(item => item.Id == change.LocalId);
            var merged = MergeNote(change, current is null ? null : FromNote(current), incoming);
            if (current is null)
            {
                current = ToNote(merged);
                notes.Add(current);
            }
            else Apply(merged, current);
            store.SaveNote(current);
        }

        var originalAssets = ResolveHistoryPath(trackingFolder, originalContext.Entry.BeforeAssetsPath, requireFile: false);
        foreach (var change in proposal.Changes.Where(change => change.IsSelected &&
                                                                 change.ChangeType != MergeChangeType.Deleted &&
                                                                 change.LocalId is not null))
        {
            RestoreAssetFolder(stageRoot, originalAssets, change.LocalId!);
        }

        var after = CaptureMappedLocal(projects, notes, people, tracking);
        WriteJsonDurably(Path.Combine(mergesFolder, beforeName), before);
        WriteJsonDurably(Path.Combine(mergesFolder, afterName), after);
        ArchiveAssetFolders(stageRoot, affectedIds, Path.Combine(mergesFolder, afterAssetsName));
        tracking.Merges.Add(new MergeHistoryEntry
        {
            MergeId = mergeId,
            PackageId = Guid.Empty,
            MergedUtc = DateTimeOffset.UtcNow,
            SourceSystemName = "Cofnięcie: " + originalContext.Entry.SourceSystemName,
            SourceLibraryId = tracking.SourceLibraryId,
            SourceSystemId = tracking.SourceSystemId,
            IsRevert = true,
            RevertsMergeId = originalContext.Entry.MergeId,
            Added = proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Added),
            Modified = proposal.Changes.Count(change => change.IsSelected && change.ChangeType is MergeChangeType.Modified or MergeChangeType.Moved),
            Deleted = proposal.Changes.Count(change => change.IsSelected && change.ChangeType == MergeChangeType.Deleted),
            Skipped = proposal.Changes.Count(change => !change.IsSelected),
            BeforeSnapshotPath = "Merges/" + beforeName,
            AfterSnapshotPath = "Merges/" + afterName,
            BeforeAssetsPath = "Merges/" + beforeAssetsName,
            AfterAssetsPath = "Merges/" + afterAssetsName
        });
        WriteJsonDurably(trackingPath, tracking);
    }

    private static TransferProject MergeProject(MergeChange change, TransferProject? mine, TransferProject theirs)
    {
        if (mine is null)
        {
            return Clone(theirs);
        }

        var baseline = change.BaseEntity as TransferProject ?? mine;
        var resolutions = Resolutions(change);
        var result = Clone(mine);
        result.Name = MergeScalar(change, "name", baseline.Name, mine.Name, theirs.Name, resolutions);
        result.ItemType = MergeScalar(change, "type", baseline.ItemType, mine.ItemType, theirs.ItemType, resolutions);
        result.ParentId = MergeScalar(change, "parent", baseline.ParentId, mine.ParentId, theirs.ParentId, resolutions);
        result.IsArchived = MergeScalar(change, "archived", baseline.IsArchived, mine.IsArchived, theirs.IsArchived, resolutions);
        result.People = MergeList(change, "people", baseline.People, mine.People, theirs.People, resolutions);
        result.SystemIds = MergeList(change, "systems", baseline.SystemIds, mine.SystemIds, theirs.SystemIds, resolutions);
        result.Description = ThreeWayTextMerge.Merge(
            baseline.Description, mine.Description, theirs.Description,
            change.SourceId + ":description", "Treść projektu / taski").Resolve(resolutions);
        result.Checklist = FrontMatter.Parse(result.Description).Checklist.Select(TransferModelCloner.From).ToList();
        return result;
    }

    private static TransferNote MergeNote(MergeChange change, TransferNote? mine, TransferNote theirs)
    {
        if (mine is null)
        {
            return Clone(theirs);
        }

        var baseline = change.BaseEntity as TransferNote ?? mine;
        var resolutions = Resolutions(change);
        var result = Clone(mine);
        result.Title = MergeScalar(change, "title", baseline.Title, mine.Title, theirs.Title, resolutions);
        result.Tags = MergeList(change, "tags", baseline.Tags, mine.Tags, theirs.Tags, resolutions);
        result.People = MergeList(change, "people", baseline.People, mine.People, theirs.People, resolutions);
        result.RelatedNoteIds = MergeList(change, "relations", baseline.RelatedNoteIds, mine.RelatedNoteIds, theirs.RelatedNoteIds, resolutions);
        result.Body = ThreeWayTextMerge.Merge(
            baseline.Body, mine.Body, theirs.Body,
            change.SourceId + ":body", "Treść notatki / taski").Resolve(resolutions);
        result.Checklist = FrontMatter.Parse(result.Body).Checklist.Select(TransferModelCloner.From).ToList();
        return result;
    }

    private static TransferPerson MergePerson(MergeChange change, TransferPerson? mine, TransferPerson theirs)
    {
        if (mine is null)
        {
            return Clone(theirs);
        }

        var baseline = change.BaseEntity as TransferPerson ?? mine;
        var resolutions = Resolutions(change);
        var result = Clone(mine);
        result.Name = MergeScalar(change, "name", baseline.Name, mine.Name, theirs.Name, resolutions);
        result.Role = MergeScalar(change, "role", baseline.Role, mine.Role, theirs.Role, resolutions);
        result.Description = ThreeWayTextMerge.Merge(
            baseline.Description, mine.Description, theirs.Description,
            change.SourceId + ":description", "Opis osoby").Resolve(resolutions);
        result.AvatarPath = MergeScalar(change, "avatar", baseline.AvatarPath, mine.AvatarPath, theirs.AvatarPath, resolutions);
        return result;
    }

    private static T MergeScalar<T>(
        MergeChange change,
        string field,
        T baseline,
        T mine,
        T theirs,
        IReadOnlyDictionary<string, MergeResolution> resolutions)
    {
        if (Equals(mine, baseline)) return theirs;
        if (Equals(theirs, baseline) || Equals(mine, theirs)) return mine;
        var conflict = change.Conflicts.FirstOrDefault(item => item.Field == field)
            ?? throw new InvalidOperationException($"Brak opisu konfliktu pola {field}.");
        return resolutions[conflict.Id] switch
        {
            MergeResolution.Mine => mine,
            MergeResolution.Theirs => theirs,
            _ => throw new InvalidOperationException($"Pole „{conflict.Label}” wymaga wyboru jednej wersji.")
        };
    }

    private static List<string> MergeList(
        MergeChange change,
        string field,
        IReadOnlyCollection<string> baseline,
        IReadOnlyCollection<string> mine,
        IReadOnlyCollection<string> theirs,
        IReadOnlyDictionary<string, MergeResolution> resolutions)
    {
        if (SetEquals(mine, baseline)) return theirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (SetEquals(theirs, baseline) || SetEquals(mine, theirs)) return mine.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var conflict = change.Conflicts.First(item => item.Field == field);
        return resolutions[conflict.Id] switch
        {
            MergeResolution.Mine => mine.ToList(),
            MergeResolution.Theirs => theirs.ToList(),
            MergeResolution.Both => mine.Concat(theirs).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            _ => throw new InvalidOperationException($"Pole „{conflict.Label}” nie zostało rozwiązane.")
        };
    }

    private static IReadOnlyDictionary<string, MergeResolution> Resolutions(MergeChange change) =>
        change.Conflicts.ToDictionary(conflict => conflict.Id, conflict => conflict.Resolution, StringComparer.Ordinal);

    private static bool ShouldDelete(MergeChange change)
    {
        var conflict = change.Conflicts.FirstOrDefault(item => item.Field == "deleted");
        return conflict is null || conflict.Resolution == MergeResolution.Theirs;
    }

    private static bool ShouldApplyIncoming(MergeChange change)
    {
        var conflict = change.Conflicts.FirstOrDefault(item => item.Field == "deleted");
        return conflict is null || conflict.Resolution == MergeResolution.Theirs;
    }

    private static void AllocateMappings(
        SystemTransferSnapshot snapshot,
        ImportTrackingRecord tracking,
        IReadOnlyCollection<Project> projects,
        IReadOnlyCollection<Note> notes,
        IReadOnlyCollection<Person> people)
    {
        var used = projects.Select(item => item.Id).Concat(notes.Select(item => item.Id)).Concat(people.Select(item => item.Id))
            .Concat(tracking.ProjectMap.Values).Concat(tracking.NoteMap.Values).Concat(tracking.PersonMap.Values)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var project in snapshot.Projects)
            Allocate(tracking.ProjectMap, project.Id, used);
        foreach (var note in snapshot.Notes)
            Allocate(tracking.NoteMap, note.Id, used);
        foreach (var person in snapshot.People)
            Allocate(tracking.PersonMap, person.Id, used);
    }

    private static void Allocate(IDictionary<string, string> mapping, string sourceId, ISet<string> used)
    {
        if (mapping.ContainsKey(sourceId))
        {
            return;
        }

        string id;
        do
        {
            id = SlugHelper.NewId();
        } while (!used.Add(id));
        mapping[sourceId] = id;
    }

    private static SystemTransferSnapshot MapIncoming(
        SystemTransferSnapshot source,
        ImportTrackingRecord tracking,
        IReadOnlyCollection<Project> currentProjects,
        IReadOnlyCollection<Person> currentPeople)
    {
        var projectSlugs = currentProjects.ToDictionary(project => project.Id, project => project.Slug,
            StringComparer.OrdinalIgnoreCase);
        var usedProjectSlugs = currentProjects.Select(project => project.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceProjectSlugs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in source.Projects)
        {
            var localId = tracking.ProjectMap[project.Id];
            if (!projectSlugs.TryGetValue(localId, out var localSlug))
            {
                localSlug = SlugHelper.Unique(project.Slug, usedProjectSlugs);
                usedProjectSlugs.Add(localSlug);
                projectSlugs[localId] = localSlug;
            }
            sourceProjectSlugs[project.Slug] = localSlug;
        }

        var personSlugs = currentPeople.ToDictionary(person => person.Id, person => person.Slug,
            StringComparer.OrdinalIgnoreCase);
        var usedPersonSlugs = currentPeople.Select(person => person.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourcePersonSlugs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var person in source.People)
        {
            var localId = tracking.PersonMap[person.Id];
            if (!personSlugs.TryGetValue(localId, out var localSlug))
            {
                localSlug = SlugHelper.Unique(person.Slug, usedPersonSlugs);
                usedPersonSlugs.Add(localSlug);
                personSlugs[localId] = localSlug;
            }
            sourcePersonSlugs[person.Slug] = localSlug;
        }

        var result = new SystemTransferSnapshot();
        foreach (var person in source.People)
        {
            var localId = tracking.PersonMap[person.Id];
            var mapped = Clone(person);
            mapped.Id = localId;
            mapped.Slug = personSlugs[localId];
            mapped.AvatarPath = RewriteAssetPath(mapped.AvatarPath, person.Id, localId);
            result.People.Add(mapped);
        }
        foreach (var project in source.Projects)
        {
            var localId = tracking.ProjectMap[project.Id];
            var mapped = Clone(project);
            mapped.Id = localId;
            mapped.Slug = projectSlugs[localId];
            mapped.ParentId = project.ParentId is not null && tracking.ProjectMap.TryGetValue(project.ParentId, out var parent)
                ? parent : null;
            mapped.SystemIds = project.SystemIds.Where(tracking.ProjectMap.ContainsKey)
                .Select(id => tracking.ProjectMap[id]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            mapped.People = MapPeople(project.People, sourcePersonSlugs);
            mapped.Description = RewriteTaskPeople(project.Description, sourcePersonSlugs);
            mapped.Checklist = FrontMatter.Parse(mapped.Description).Checklist.Select(TransferModelCloner.From).ToList();
            result.Projects.Add(mapped);
        }
        foreach (var note in source.Notes)
        {
            var localId = tracking.NoteMap[note.Id];
            var mapped = Clone(note);
            mapped.Id = localId;
            mapped.Tags = note.Tags.Select(tag => sourceProjectSlugs.GetValueOrDefault(tag, tag))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            mapped.People = MapPeople(note.People, sourcePersonSlugs);
            mapped.RelatedNoteIds = note.RelatedNoteIds.Where(tracking.NoteMap.ContainsKey)
                .Select(id => tracking.NoteMap[id]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            mapped.Body = RewriteTaskPeople(RewriteAssetPath(note.Body, note.Id, localId), sourcePersonSlugs);
            mapped.Checklist = FrontMatter.Parse(mapped.Body).Checklist.Select(TransferModelCloner.From).ToList();
            result.Notes.Add(mapped);
        }
        foreach (var (sourceId, position) in source.Positions)
        {
            var localId = tracking.ProjectMap.GetValueOrDefault(sourceId) ?? tracking.NoteMap.GetValueOrDefault(sourceId);
            if (localId is not null)
            {
                result.Positions[localId] = new TransferGraphPosition { X = position.X, Y = position.Y };
            }
        }
        return result;
    }

    private static SystemTransferSnapshot CaptureMappedLocal(
        IReadOnlyCollection<Project> projects,
        IReadOnlyCollection<Note> notes,
        IReadOnlyCollection<Person> people,
        ImportTrackingRecord tracking)
    {
        var projectIds = tracking.ProjectMap.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var noteIds = tracking.NoteMap.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var personIds = tracking.PersonMap.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SystemTransferSnapshot
        {
            Projects = projects.Where(project => projectIds.Contains(project.Id)).Select(FromProject).ToList(),
            Notes = notes.Where(note => noteIds.Contains(note.Id)).Select(FromNote).ToList(),
            People = people.Where(person => personIds.Contains(person.Id)).Select(FromPerson).ToList()
        };
    }

    private static TransferProject FromProject(Project project) => new()
    {
        Id = project.Id, Name = project.Name, Slug = project.Slug, Description = project.Description,
        People = project.People.ToList(), SystemIds = project.SystemIds.ToList(),
        Checklist = project.Checklist.Select(TransferModelCloner.From).ToList(), IsArchived = project.IsArchived,
        ParentId = project.ParentId, ItemType = project.ItemType, Created = project.Created, Modified = project.Modified
    };

    private static TransferNote FromNote(Note note) => new()
    {
        Id = note.Id, Title = note.Title, Body = note.Body, Tags = note.Tags.ToList(), People = note.People.ToList(),
        RelatedNoteIds = note.RelatedNoteIds.ToList(), Checklist = note.Checklist.Select(TransferModelCloner.From).ToList(),
        Created = note.Created, Modified = note.Modified
    };

    private static TransferPerson FromPerson(Person person) => new()
    {
        Id = person.Id, Name = person.Name, Slug = person.Slug, Role = person.Role,
        Description = person.Description, AvatarPath = person.AvatarPath, Created = person.Created, Modified = person.Modified
    };

    private static Project ToProject(TransferProject item) => new()
    {
        Id = item.Id, Name = item.Name, Slug = item.Slug, Description = item.Description,
        People = item.People.ToList(), SystemIds = item.SystemIds.ToList(),
        Checklist = item.Checklist.Select(TransferModelCloner.To).ToList(), IsArchived = item.IsArchived,
        ParentId = item.ParentId, ItemType = item.ItemType, Created = item.Created, Modified = item.Modified
    };

    private static Note ToNote(TransferNote item) => new()
    {
        Id = item.Id, Title = item.Title, Body = item.Body, Tags = item.Tags.ToList(), People = item.People.ToList(),
        RelatedNoteIds = item.RelatedNoteIds.ToList(), Checklist = item.Checklist.Select(TransferModelCloner.To).ToList(),
        Created = item.Created, Modified = item.Modified
    };

    private static Person ToPerson(TransferPerson item) => new()
    {
        Id = item.Id, Name = item.Name, Slug = item.Slug, Role = item.Role,
        Description = item.Description, AvatarPath = item.AvatarPath, Created = item.Created, Modified = item.Modified
    };

    private static void Apply(TransferProject source, Project destination)
    {
        destination.Name = source.Name; destination.Slug = source.Slug; destination.Description = source.Description;
        destination.People = source.People.ToList(); destination.SystemIds = source.SystemIds.ToList();
        destination.Checklist = source.Checklist.Select(TransferModelCloner.To).ToList(); destination.IsArchived = source.IsArchived;
        destination.ParentId = source.ParentId; destination.ItemType = source.ItemType;
    }

    private static void Apply(TransferNote source, Note destination)
    {
        destination.Title = source.Title; destination.Body = source.Body; destination.Tags = source.Tags.ToList();
        destination.People = source.People.ToList(); destination.RelatedNoteIds = source.RelatedNoteIds.ToList();
        destination.Checklist = source.Checklist.Select(TransferModelCloner.To).ToList();
    }

    private static void Apply(TransferPerson source, Person destination)
    {
        destination.Name = source.Name; destination.Slug = source.Slug; destination.Role = source.Role;
        destination.Description = source.Description; destination.AvatarPath = source.AvatarPath;
    }

    private static void CopyAssets(LoadedSystemTransferPackage package, string sourceId, string localId, string root)
    {
        var folder = Path.Combine(root, "Assets", localId);
        SystemTransferPackageService.ExtractAssets(package, sourceId, folder);
    }

    private static void UpdateGraphPositions(
        string stageRoot,
        string finalRoot,
        MergeProposal proposal,
        IReadOnlyCollection<Project> projects,
        IReadOnlyCollection<Note> notes)
    {
        var statePath = Path.Combine(stageRoot, "app-state.json");
        AppState state;
        if (File.Exists(statePath))
        {
            state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(statePath), SystemTransferPackageService.JsonOptions)
                ?? new AppState();
        }
        else
        {
            state = new AppState();
        }
        state.NodePositions = new Dictionary<string, GraphPosition>(state.NodePositions ?? [], StringComparer.OrdinalIgnoreCase);
        var addedIds = proposal.Changes.Where(change => change.IsSelected && change.ChangeType == MergeChangeType.Added)
            .Select(change => change.LocalId).Where(id => id is not null).Select(id => id!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incomingPositions = proposal.IncomingLocal.Positions.Where(pair => addedIds.Contains(pair.Key)).ToList();
        if (incomingPositions.Count > 0)
        {
            var minX = incomingPositions.Min(pair => pair.Value.X);
            var minY = incomingPositions.Min(pair => pair.Value.Y);
            var existing = state.NodePositions.Where(pair => !addedIds.Contains(pair.Key)).Select(pair => pair.Value).ToList();
            var targetX = existing.Count == 0 ? 120 : existing.Max(position => position.X) + 260;
            var targetY = existing.Count == 0 ? 120 : Math.Max(80, existing.Min(position => position.Y));
            foreach (var (id, position) in incomingPositions)
            {
                state.NodePositions[id] = new GraphPosition
                {
                    Id = id,
                    X = targetX + position.X - minX,
                    Y = targetY + position.Y - minY
                };
            }
        }
        state.DataFolder = finalRoot;
        WriteJsonDurably(statePath, state);
    }

    private static ImportTrackingRecord? LoadTracking(string root, SystemTransferManifest manifest)
    {
        var path = Path.Combine(TrackingFolder(root, manifest), TrackingFileName);
        if (!File.Exists(path)) return null;
        var tracking = JsonSerializer.Deserialize<ImportTrackingRecord>(File.ReadAllText(path),
            SystemTransferPackageService.JsonOptions)
            ?? throw new InvalidDataException("Rejestr poprzednich scaleń jest pusty.");
        if (tracking.Version != 1 || tracking.SourceLibraryId != manifest.SourceLibraryId ||
            !string.Equals(tracking.SourceSystemId, manifest.SourceSystemId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Rejestr poprzednich scaleń nie pasuje do paczki.");
        }
        NormalizeMaps(tracking);
        return tracking;
    }

    private static ImportTrackingRecord NewTracking(SystemTransferManifest manifest) => new()
    {
        SourceLibraryId = manifest.SourceLibraryId,
        SourceSystemId = manifest.SourceSystemId
    };

    private static string TrackingFolder(string root, SystemTransferManifest manifest)
    {
        SafeFileStorage.ValidateFileToken(manifest.SourceSystemId, "identyfikator systemu źródłowego");
        return Path.Combine(root, "Imports", manifest.SourceLibraryId.ToString("N"), manifest.SourceSystemId);
    }

    private static void PruneMappings(ImportTrackingRecord tracking, SystemTransferSnapshot after)
    {
        Prune(tracking.ProjectMap, after.Projects.Select(item => item.Id));
        Prune(tracking.NoteMap, after.Notes.Select(item => item.Id));
        Prune(tracking.PersonMap, after.People.Select(item => item.Id));

        static void Prune(Dictionary<string, string> map, IEnumerable<string> existingIds)
        {
            var existing = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in map.Where(pair => !existing.Contains(pair.Value)).Select(pair => pair.Key).ToList())
                map.Remove(key);
        }
    }

    private static void NormalizeMaps(ImportTrackingRecord tracking)
    {
        tracking.ProjectMap = new Dictionary<string, string>(tracking.ProjectMap ?? [], StringComparer.OrdinalIgnoreCase);
        tracking.NoteMap = new Dictionary<string, string>(tracking.NoteMap ?? [], StringComparer.OrdinalIgnoreCase);
        tracking.PersonMap = new Dictionary<string, string>(tracking.PersonMap ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ActiveSources(
        IReadOnlyDictionary<string, string> mapping,
        IEnumerable<string> activeLocalIds,
        IEnumerable<MergeChange> changes,
        MergeEntityKind kind)
    {
        var activeIds = activeLocalIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = mapping.Where(pair => activeIds.Contains(pair.Value)).Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var deletion in changes.Where(change => change.Kind == kind && change.IsSelected &&
                                                          change.ChangeType == MergeChangeType.Deleted))
            result.Remove(deletion.SourceId);
        return result;
    }

    private static string? PersonSourceId(
        SystemTransferSnapshot incomingLocal,
        ImportTrackingRecord tracking,
        string localSlug)
    {
        var localId = incomingLocal.People.FirstOrDefault(person =>
            string.Equals(person.Slug, localSlug, StringComparison.OrdinalIgnoreCase))?.Id;
        return SourceForLocal(tracking.PersonMap, localId);
    }

    private static string? ProjectSourceId(
        SystemTransferSnapshot incomingLocal,
        ImportTrackingRecord tracking,
        string localSlug)
    {
        var localId = incomingLocal.Projects.FirstOrDefault(project =>
            string.Equals(project.Slug, localSlug, StringComparison.OrdinalIgnoreCase))?.Id;
        return SourceForLocal(tracking.ProjectMap, localId);
    }

    private static string? SourceForLocal(IReadOnlyDictionary<string, string> mapping, string? localId) =>
        string.IsNullOrWhiteSpace(localId)
            ? null
            : mapping.FirstOrDefault(pair => string.Equals(pair.Value, localId, StringComparison.OrdinalIgnoreCase)).Key;

    private static string RewriteAssetPath(string value, string sourceId, string localId) =>
        (value ?? string.Empty)
        .Replace($"../Assets/{sourceId}/", $"../Assets/{localId}/", StringComparison.OrdinalIgnoreCase)
        .Replace($"Assets/{sourceId}/", $"Assets/{localId}/", StringComparison.OrdinalIgnoreCase);

    private static string RewriteTaskPeople(string body, IReadOnlyDictionary<string, string> slugMap) =>
        TaskPeopleMetadata.Replace(body ?? string.Empty, match =>
        {
            var mapped = PersonTagService.Parse(match.Groups["people"].Value)
                .Select(slug => slugMap.GetValueOrDefault(slug, slug))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return $"<!-- people: {string.Join(", ", mapped)} -->";
        });

    private static List<string> MapPeople(IEnumerable<string> source, IReadOnlyDictionary<string, string> slugMap) =>
        source.Select(slug => slugMap.GetValueOrDefault(slug, slug)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string ProjectTitle(TransferProject item) => item.Name;
    private static string NoteTitle(TransferNote item) => item.Title;
    private static string PersonTitle(TransferPerson item) => item.Name;

    private static bool SemanticEquals<T>(T left, T right) =>
        string.Equals(SemanticJson(left), SemanticJson(right), StringComparison.Ordinal);

    private static string SemanticJson<T>(T value)
    {
        object normalized = value switch
        {
            TransferProject item => new
            {
                item.Name, item.Slug, item.Description,
                People = Sorted(item.People), Systems = Sorted(item.SystemIds), item.IsArchived,
                item.ParentId, item.ItemType
            },
            TransferNote item => new
            {
                item.Title, item.Body, Tags = Sorted(item.Tags), People = Sorted(item.People),
                Relations = Sorted(item.RelatedNoteIds)
            },
            TransferPerson item => new { item.Name, item.Slug, item.Role, item.Description, item.AvatarPath },
            _ => value!
        };
        return JsonSerializer.Serialize(normalized, SystemTransferPackageService.JsonOptions);
    }

    private static List<string> Sorted(IEnumerable<string> values) =>
        values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool SetEquals(IEnumerable<string> left, IEnumerable<string> right) =>
        left.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(right);

    private static string Display<T>(T value) => value?.ToString() ?? "(brak)";

    private static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, SystemTransferPackageService.JsonOptions),
            SystemTransferPackageService.JsonOptions)!;

    private static void WireProposal(MergeProposal proposal)
    {
        foreach (var change in proposal.Changes)
        {
            foreach (var conflict in change.Conflicts)
            {
                conflict.ResolutionChanged += () => { };
            }
        }
    }

    private static MergeHistoryContext FindHistory(string root, Guid mergeId)
    {
        var imports = Path.Combine(root, "Imports");
        if (!Directory.Exists(imports))
        {
            throw new InvalidOperationException("Biblioteka nie ma historii scaleń.");
        }

        foreach (var path in Directory.EnumerateFiles(imports, TrackingFileName, SearchOption.AllDirectories))
        {
            ImportTrackingRecord? tracking;
            try
            {
                tracking = JsonSerializer.Deserialize<ImportTrackingRecord>(
                    File.ReadAllText(path), SystemTransferPackageService.JsonOptions);
            }
            catch
            {
                continue;
            }

            var entry = tracking?.Merges.FirstOrDefault(item => item.MergeId == mergeId);
            if (tracking is not null && entry is not null)
            {
                NormalizeMaps(tracking);
                return new MergeHistoryContext(Path.GetDirectoryName(path)!, tracking, entry);
            }
        }

        throw new InvalidOperationException("Nie znaleziono wskazanego scalenia.");
    }

    private static ImportTrackingRecord IdentityTracking(
        ImportTrackingRecord original,
        params SystemTransferSnapshot[] snapshots)
    {
        var tracking = new ImportTrackingRecord
        {
            SourceLibraryId = original.SourceLibraryId,
            SourceSystemId = original.SourceSystemId
        };
        foreach (var id in snapshots.SelectMany(snapshot => snapshot.Projects).Select(item => item.Id)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            tracking.ProjectMap[id] = id;
        foreach (var id in snapshots.SelectMany(snapshot => snapshot.Notes).Select(item => item.Id)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            tracking.NoteMap[id] = id;
        foreach (var id in snapshots.SelectMany(snapshot => snapshot.People).Select(item => item.Id)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            tracking.PersonMap[id] = id;
        return tracking;
    }

    private static SystemTransferSnapshot ReadSnapshot(string trackingFolder, string relativePath)
    {
        var path = ResolveHistoryPath(trackingFolder, relativePath, requireFile: true);
        return JsonSerializer.Deserialize<SystemTransferSnapshot>(
            File.ReadAllText(path), SystemTransferPackageService.JsonOptions)
            ?? throw new InvalidDataException("Snapshot scalenia jest pusty.");
    }

    private static string ResolveHistoryPath(string trackingFolder, string relativePath, bool requireFile)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Path.Combine(trackingFolder, "__missing_history_asset__");
        }

        var root = Path.GetFullPath(trackingFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidDataException("Ścieżka historii wychodzi poza rejestr importu.");
        }

        if (requireFile && !File.Exists(path))
        {
            throw new FileNotFoundException("Brakuje snapshotu potrzebnego do cofnięcia scalenia.", path);
        }
        return path;
    }

    private static void ArchiveAssetFolders(string libraryRoot, IEnumerable<string> ids, string destinationRoot)
    {
        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SafeFileStorage.ValidateFileToken(id, "identyfikator zasobu historii");
            var source = Path.Combine(libraryRoot, "Assets", id);
            if (!Directory.Exists(source))
            {
                continue;
            }

            CopyDirectory(source, Path.Combine(destinationRoot, id));
        }
    }

    private static void RestoreAssetFolder(string libraryRoot, string archiveRoot, string id)
    {
        SafeFileStorage.ValidateFileToken(id, "identyfikator przywracanego zasobu");
        var source = Path.Combine(archiveRoot, id);
        var destination = Path.Combine(libraryRoot, "Assets", id);
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }
        if (Directory.Exists(source))
        {
            CopyDirectory(source, destination);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var child in Directory.EnumerateDirectories(source))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
        }
    }

    private static MergeTransaction PrepareTransaction(string root, Guid mergeId)
    {
        var parent = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Brak folderu nadrzędnego biblioteki.");
        var name = Path.GetFileName(root);
        var token = mergeId.ToString("N");
        var transaction = new MergeTransaction
        {
            Root = root,
            Backup = Path.Combine(parent, $".{name}.merge-{token}.backup"),
            Stage = Path.Combine(parent, $".{name}.merge-{token}.stage"),
            Rollback = Path.Combine(parent, $".{name}.merge-{token}.rollback"),
            Phase = MergeTransactionPhase.Prepared
        };
        BackupService.ExportCopy(root, transaction.Backup);
        BackupService.RestoreCopy(transaction.Backup, transaction.Stage);
        WriteJournal(transaction);
        return transaction;
    }

    private static void CommitTransaction(MergeTransaction transaction)
    {
        Directory.Move(transaction.Root, transaction.Rollback);
        transaction.Phase = MergeTransactionPhase.OriginalMoved;
        WriteJournal(transaction);
        Directory.Move(transaction.Stage, transaction.Root);
        transaction.Phase = MergeTransactionPhase.Committed;
        WriteJournal(transaction);
        try
        {
            ValidateStaging(transaction.Root);
        }
        catch
        {
            var failed = transaction.Stage + ".failed";
            if (!Directory.Exists(failed))
            {
                Directory.Move(transaction.Root, failed);
            }
            Directory.Move(transaction.Rollback, transaction.Root);
            transaction.Phase = MergeTransactionPhase.Prepared;
            WriteJournal(transaction);
            throw;
        }
        TryDeleteDirectory(transaction.Rollback);
        TryDeleteDirectory(transaction.Backup);
        TryDeleteFile(JournalPath(transaction.Root));
    }

    private static void RollbackTransaction(MergeTransaction transaction)
    {
        try
        {
            if (!Directory.Exists(transaction.Root) && Directory.Exists(transaction.Rollback))
                Directory.Move(transaction.Rollback, transaction.Root);
            else if (Directory.Exists(transaction.Root) && Directory.Exists(transaction.Rollback))
            {
                var failed = transaction.Stage + ".failed";
                if (!Directory.Exists(failed)) Directory.Move(transaction.Root, failed);
                Directory.Move(transaction.Rollback, transaction.Root);
            }
            TryDeleteDirectory(transaction.Stage);
            TryDeleteDirectory(transaction.Backup);
            if (Directory.Exists(transaction.Root)) TryDeleteFile(JournalPath(transaction.Root));
        }
        catch
        {
            // The durable journal is deliberately left for startup recovery.
        }
    }

    private static void ValidateStaging(string root)
    {
        var store = new MarkdownStore(root);
        var projects = store.LoadProjects();
        var notes = store.LoadNotes();
        var people = store.LoadPeople();
        var ids = projects.Select(item => item.Id).Concat(notes.Select(item => item.Id)).Concat(people.Select(item => item.Id)).ToList();
        if (ids.Count != ids.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new InvalidDataException("Scalenie utworzyło powtórzone identyfikatory.");
        var projectIds = projects.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (projects.Any(project => project.ParentId is not null && !projectIds.Contains(project.ParentId)))
            throw new InvalidDataException("Scalenie utworzyło element bez istniejącego rodzica.");
    }

    private static void WriteJournal(MergeTransaction transaction) =>
        WriteJsonDurably(JournalPath(transaction.Root), transaction);

    private static string JournalPath(string root)
    {
        var parent = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Brak folderu nadrzędnego biblioteki.");
        return Path.Combine(parent, "." + Path.GetFileName(root) + ".merge-journal.json");
    }

    private static void WriteJsonDurably<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".partial";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SystemTransferPackageService.JsonOptions);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

public sealed record MergeApplyResult(Guid MergeId, int Added, int Modified, int Deleted, int Skipped);

internal sealed record MergeHistoryContext(
    string TrackingFolder,
    ImportTrackingRecord Tracking,
    MergeHistoryEntry Entry);

public enum MergeTransactionPhase
{
    Prepared,
    OriginalMoved,
    Committed
}

public sealed class MergeTransaction
{
    public string Root { get; set; } = string.Empty;
    public string Backup { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Rollback { get; set; } = string.Empty;
    public MergeTransactionPhase Phase { get; set; }
}
