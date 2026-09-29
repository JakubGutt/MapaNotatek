using MapaNotatek.Models;
using MapaNotatek.Services;

var tests = new (string Name, Action Run)[]
{
    ("task między akapitami zachowuje pozycję", ChecklistPositionRoundTrip),
    ("toggle zmienia marker taska w miejscu", ChecklistToggleInPlace),
    ("ręczna zmiana Body wygrywa ze starym indeksem", ManualBodyEditWins),
    ("nowy task z panelu jest dopisywany tylko raz", NewPanelTaskIsAppendedOnce),
    ("legacy task nie jest duplikowany", LegacyTaskIsNotDuplicated),
    ("metadane projektu przechodzą round-trip", ProjectMetadataRoundTrip),
    ("typy architektury przechodzą round-trip", ArchitectureTypesRoundTrip),
    ("hierarchia architektury pilnuje dozwolonych relacji", ArchitectureHierarchyRules),
    ("przypisania osób są osobne od tagów", PersonAssignmentsRoundTrip),
    ("nazwa osoby rozwiązuje się do stabilnego identyfikatora", PersonNameResolvesToStableSlug),
    ("osoby przypisane do zadań przechodzą round-trip", TaskPeopleRoundTrip),
    ("priorytet zadań przechodzi round-trip", TaskPriorityRoundTrip),
    ("rejestr osób zapisuje profil i awatar", PersonStoreRoundTrip),
    ("ponowny zapis tworzy kopię awaryjną", AtomicSaveCreatesBackup),
    ("nieudana zmiana nazwy zachowuje stary plik", FailedRenamePreservesOriginal),
    ("błąd archiwizacji nazwy wycofuje nowy plik", FailedRenameArchiveRollsBack),
    ("slug nie może wyjść poza Projects", ProjectSlugCannotEscapeRoot),
    ("zewnętrzny FilePath nie może zostać skasowany", ExternalFilePathIsRejected),
    ("katalog główny systemu jest poprawnym rootem", FileSystemRootContainmentWorks),
    ("błąd pliku notatki jest raportowany", ReadFailureIsReported),
    ("uszkodzona notatka jest odzyskiwana z .bak", NoteRecoversFromBackup),
    ("stan aplikacji odzyskuje się z .bak", AppStateRecoversFromBackup),
    ("wygląd edytora jest zapamiętywany lokalnie", EditorAppearanceRoundTrip),
    ("widoczność paneli bocznych jest zapamiętywana", SidePanelVisibilityRoundTrip),
    ("uszkodzony stan bez backupu przerywa ładowanie", CorruptStateWithoutBackupThrows),
    ("odzyskiwanie stanu zachowuje uszkodzony plik", RecoverStatePreservesBrokenFile),
    ("niezamknięty front matter jest błędem", UnterminatedFrontMatterThrows),
    ("kopia zawiera tylko dane aplikacji i poprawny manifest", BackupContainsOnlyApplicationData),
    ("kopia wykrywa późniejszą modyfikację pliku", BackupDetectsTampering),
    ("zweryfikowaną kopię można przywrócić do pustego folderu", BackupRestoresToEmptyFolder),
    ("kopia nie może powstać wewnątrz biblioteki", BackupCannotBeInsideLibrary),
    ("kopii nie można przywrócić do jej wnętrza", BackupRestoreCannotTargetInsideBackup),
    ("kopia odrzuca dowiązania symboliczne", BackupRejectsSymbolicLinks),
    ("obraz jest kopiowany do lokalnego Assets", AttachmentIsImportedLocally),
    ("fałszywy obraz i złośliwy identyfikator są odrzucane", InvalidAttachmentIsRejected),
    ("historia deduplikuje i ogranicza liczbę rewizji", RevisionHistoryIsBounded),
    ("folder z obcą zawartością nie jest biblioteką", UnrelatedFolderIsRejected),
    ("kod aplikacji nie zawiera klientów sieciowych", OfflineSourcePolicy),
    ("projekt można przenieść do kosza i przywrócić", ProjectTrashRoundTrip),
    ("przywrócony projekt zachowuje unikalny slug w pliku", ProjectTrashCollisionPersistsUniqueSlug),
    ("kosz przenosi kopię awaryjną notatki", NoteTrashMovesBackup),
    ("projekt w koszu odzyskuje się z kopii awaryjnej", TrashedProjectRecoversFromBackup),
    ("zewnętrzna zmiana pliku nie jest nadpisywana", ExternalModificationCreatesConflict),
    ("atomowa podmiana cofa wykrytą zmianę wyścigową", AtomicReplaceRestoresRacedFile),
    ("edytor wizualny zachowuje strukturę dokumentu", VisualDocumentRoundTrip),
    ("edytor wizualny zachowuje tabelę i kod", VisualDocumentTableAndCodeRoundTrip),
    ("edytor wizualny zachowuje pionową kreskę w tabeli", VisualDocumentEscapedTableCell),
    ("outline zawiera tylko nagłówki", VisualDocumentOutline),
    ("wbudowane szablony tworzą poprawne dokumenty", BuiltInTemplatesAreValid),
    ("kartka edytora ma stabilną szerokość", EditorCanvasHasStableWidth),
    ("formatowanie działa na zaznaczeniu bez tekstów zastępczych", InlineFormattingUsesRealSelection),
    ("edytor wizualny udostępnia komórki dokumentu", EditorExposesDocumentCells),
    ("enter kontynuuje listy i zadania w komórkach", EnterContinuesCellLists),
    ("enter kontynuuje listy w źródle Markdown", EnterContinuesMarkdownLists),
    ("graf ma czytelne sterowanie i mapuje wikilinki", GraphWorkspaceIsDiscoverable),
    ("graf i drzewo rozróżniają typy architektury", ArchitectureTypesAreWired),
    ("lewe drzewo pokazuje notatki w kontekście projektów", NavigationTreeIncludesNotes),
    ("kafelki zadań obsługują ręczne ustawianie priorytetu", TaskPriorityDragIsWired),
    ("filtr grafu obejmuje tylko wybrane drzewo projektu", GraphProjectScopeIsolated),
    ("nowe karty grafu nie nakładają się w układzie", GraphCardsHaveBreathingRoom),
    ("panel osób jest podłączony do nawigacji i szczegółów", PeoplePanelIsWired),
    ("przypisania osób korzystają wyłącznie z selektora rejestru", PersonAssignmentsUseRegistryPicker),
    ("kliknięcie poza polem kończy edycję w całej aplikacji", ClickOutsideDismissesTextEditing),
    ("wyszukiwarka obsługuje filtry i pełne frazy", SearchFiltersAndPhrases),
    ("wyszukiwarka obsługuje wykluczenia i zadania", SearchExclusionsAndTasks),
    ("wyszukiwarka rozróżnia foldery i projekty", SearchProjectTypes)
};

var failed = 0;
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL  {name}");
        Console.Error.WriteLine(ex);
    }
}

Console.WriteLine($"\nWynik: {tests.Length - failed}/{tests.Length} testów zaliczonych.");
return failed == 0 ? 0 : 1;

static void VisualDocumentRoundTrip()
{
    const string source = "# Plan\n\nAkapit z **ważnym** tekstem.\n\n- punkt\n- [x] gotowe\n\n> cytat\n\n![diagram](../Assets/n1/diagram.png)";
    var blocks = VisualDocumentService.Parse(source);

    Equal(DocumentBlockKind.Heading1, blocks[0].Kind);
    Equal(DocumentBlockKind.Paragraph, blocks[1].Kind);
    Equal(DocumentBlockKind.Bullet, blocks[2].Kind);
    Equal(DocumentBlockKind.Checklist, blocks[3].Kind);
    True(blocks[3].IsChecked, "Stan checklisty powinien przejść do bloku.");
    Equal(DocumentBlockKind.Quote, blocks[4].Kind);
    Equal(DocumentBlockKind.Image, blocks[5].Kind);

    var reparsed = VisualDocumentService.Parse(VisualDocumentService.Serialize(blocks));
    Equal(blocks.Count, reparsed.Count);
    Equal("Akapit z **ważnym** tekstem.", reparsed[1].Text);
    Equal("../Assets/n1/diagram.png", reparsed[5].ImagePath);
}

static void VisualDocumentTableAndCodeRoundTrip()
{
    const string source = "| A | B |\n| --- | --- |\n| 1 | 2 |\n\n```csharp\nvar x = 1;\n```";
    var blocks = VisualDocumentService.Parse(source);

    Equal(2, blocks.Count);
    Equal(DocumentBlockKind.Table, blocks[0].Kind);
    Equal(2, blocks[0].Cells.Count);
    Equal("2", blocks[0].Cells[1][1]);
    Equal(DocumentBlockKind.Code, blocks[1].Kind);
    Equal("csharp", blocks[1].Language);

    var serialized = VisualDocumentService.Serialize(blocks);
    True(serialized.Contains("| --- | --- |", StringComparison.Ordinal));
    True(serialized.Contains("```csharp", StringComparison.Ordinal));
}

static void EnterContinuesCellLists()
{
    var bullet = new DocumentBlock { Kind = DocumentBlockKind.Bullet, Text = "pierwszy punkt" };
    var bulletEdit = ListEditingService.SplitBlock(bullet, bullet.Text, bullet.Text.Length, bullet.Text.Length);
    Equal(DocumentBlockKind.Bullet, bulletEdit.CurrentKind);
    Equal("pierwszy punkt", bulletEdit.CurrentText);
    Equal(DocumentBlockKind.Bullet, bulletEdit.FollowingBlock!.Kind);
    Equal(string.Empty, bulletEdit.FollowingBlock.Text);

    var numbered = new DocumentBlock { Kind = DocumentBlockKind.Numbered, Text = "pierwsza druga" };
    var numberedEdit = ListEditingService.SplitBlock(numbered, numbered.Text, 9, 9);
    Equal("pierwsza ", numberedEdit.CurrentText);
    Equal(DocumentBlockKind.Numbered, numberedEdit.FollowingBlock!.Kind);
    Equal("druga", numberedEdit.FollowingBlock.Text);

    var checklist = new DocumentBlock { Kind = DocumentBlockKind.Checklist, Text = "gotowe", IsChecked = true };
    var checklistEdit = ListEditingService.SplitBlock(checklist, checklist.Text, checklist.Text.Length, checklist.Text.Length);
    True(checklistEdit.CurrentIsChecked);
    Equal(DocumentBlockKind.Checklist, checklistEdit.FollowingBlock!.Kind);
    False(checklistEdit.FollowingBlock.IsChecked, "Nowe zadanie powinno być niezaznaczone.");

    var empty = new DocumentBlock { Kind = DocumentBlockKind.Checklist, Text = string.Empty };
    var emptyEdit = ListEditingService.SplitBlock(empty, empty.Text, 0, 0);
    Equal(DocumentBlockKind.Paragraph, emptyEdit.CurrentKind);
    True(emptyEdit.FollowingBlock is null, "Enter na pustym elemencie powinien zakończyć listę.");
}

static void EnterContinuesMarkdownLists()
{
    const string numbered = "1. Pierwszy";
    var numberedEdit = ListEditingService.ContinueMarkdownList(numbered, numbered.Length, numbered.Length);
    True(numberedEdit.Changed);
    Equal("1. Pierwszy\n2. ", numberedEdit.Text);
    Equal(numberedEdit.Text.Length, numberedEdit.CaretIndex);

    const string checklist = "- [x] Gotowe";
    var checklistEdit = ListEditingService.ContinueMarkdownList(checklist, checklist.Length, checklist.Length);
    Equal("- [x] Gotowe\n- [ ] ", checklistEdit.Text);

    const string empty = "- ";
    var emptyEdit = ListEditingService.ContinueMarkdownList(empty, empty.Length, empty.Length);
    Equal(string.Empty, emptyEdit.Text);
    Equal(0, emptyEdit.CaretIndex);
}

static void VisualDocumentEscapedTableCell()
{
    const string source = "| Pole | Wartość |\n| --- | --- |\n| A | lewa \\| prawa |";
    var blocks = VisualDocumentService.Parse(source);

    Equal(1, blocks.Count);
    Equal(DocumentBlockKind.Table, blocks[0].Kind);
    Equal(2, blocks[0].Cells[1].Count);
    Equal("lewa | prawa", blocks[0].Cells[1][1]);
    True(VisualDocumentService.Serialize(blocks).Contains("lewa \\| prawa", StringComparison.Ordinal));
}

static void VisualDocumentOutline()
{
    var blocks = VisualDocumentService.Parse("# Pierwszy\n\ntekst\n\n## Drugi **ważny**\n\n- punkt");
    var outline = VisualDocumentService.BuildOutline(blocks);

    Equal(2, outline.Count);
    Equal("Pierwszy", outline[0].Title);
    Equal(1, outline[0].Level);
    Equal("Drugi ważny", outline[1].Title);
    Equal(2, outline[1].Level);
}

static void BuiltInTemplatesAreValid()
{
    True(NoteTemplateCatalog.BuiltIn.Count >= 5, "Powinno istnieć co najmniej pięć szablonów startowych.");
    foreach (var template in NoteTemplateCatalog.BuiltIn)
    {
        True(!string.IsNullOrWhiteSpace(template.Id), "Szablon musi mieć identyfikator.");
        True(!string.IsNullOrWhiteSpace(template.Name), "Szablon musi mieć nazwę.");
        var blocks = VisualDocumentService.Parse(template.Body);
        True(blocks.Count > 0, $"Szablon {template.Id} musi tworzyć dokument.");
        True(VisualDocumentService.Serialize(blocks).Length > 0, $"Szablon {template.Id} nie może znikać po zapisie.");
    }

    Equal("Spotkanie — 2026-09-24", NoteTemplateCatalog.ResolveTitle(NoteTemplateCatalog.Get("meeting"), new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero)));
}

static void EditorCanvasHasStableWidth()
{
    var root = FindRepositoryRoot();
    var xaml = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "EditorPanel.axaml"));
    True(
        System.Text.RegularExpressions.Regex.IsMatch(
            xaml,
            "x:Name=\"DocumentPaper\"[\\s\\S]{0,180}HorizontalAlignment=\"Stretch\""),
        "Kontener dokumentu musi rozciągać się do ograniczonej szerokości zamiast mierzyć się treścią.");
    True(
        System.Text.RegularExpressions.Regex.IsMatch(
            xaml,
            "x:Name=\"NoteBodyBox\"[\\s\\S]{0,800}ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\""),
        "Długi wiersz nie może włączać poziomego przewijania całej notatki.");
}

static void InlineFormattingUsesRealSelection()
{
    var bold = InlineFormattingService.Toggle("Ala ma kota", 0, 3, "**", "**");
    True(bold.Changed);
    Equal("**Ala** ma kota", bold.Text);
    Equal(2, bold.SelectionStart);
    Equal(5, bold.SelectionEnd);

    var unbold = InlineFormattingService.Toggle(
        bold.Text,
        bold.SelectionStart,
        bold.SelectionEnd,
        "**",
        "**");
    True(unbold.Changed);
    Equal("Ala ma kota", unbold.Text);
    Equal(0, unbold.SelectionStart);
    Equal(3, unbold.SelectionEnd);

    var empty = InlineFormattingService.Toggle("Bez zmian", 4, 4, "*", "*");
    False(empty.Changed, "Brak zaznaczenia nie może wstawiać przykładowego słowa.");
    Equal("Bez zmian", empty.Text);

    var italicInsideBold = InlineFormattingService.Toggle("**Ala**", 2, 5, "*", "*");
    Equal("***Ala***", italicInsideBold.Text);
    var combined = WikiLinkService.ParseInlineSpans(italicInsideBold.Text).Single();
    True(combined.Bold && combined.Italic, "Podgląd powinien łączyć pogrubienie z kursywą.");
}

static void EditorExposesDocumentCells()
{
    var root = FindRepositoryRoot();
    var view = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "EditorPanel.axaml"));
    var code = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "EditorPanel.axaml.cs"));

    True(view.Contains("DODAJ KOMÓRKĘ", StringComparison.Ordinal));
    True(view.Contains("x:Name=\"EditorFontPicker\"", StringComparison.Ordinal));
    True(view.Contains("OnAddCodeBlock", StringComparison.Ordinal));
    True(code.Contains("InlineFormattingService.Toggle", StringComparison.Ordinal));
    False(code.Contains("WrapSelection(\"**\", \"**\", \"pogrubienie\")", StringComparison.Ordinal),
        "Przycisk pogrubienia nie może wstawiać tekstu zastępczego.");
}

static void GraphWorkspaceIsDiscoverable()
{
    var root = FindRepositoryRoot();
    var viewPath = Path.Combine(root, "src", "MapaNotatek", "Views", "GraphView.axaml");
    var codePath = Path.Combine(root, "src", "MapaNotatek", "Views", "GraphView.axaml.cs");
    var windowCodePath = Path.Combine(root, "src", "MapaNotatek", "MainWindow.axaml.cs");
    var xaml = File.ReadAllText(viewPath);
    var code = File.ReadAllText(codePath);
    var windowCode = File.ReadAllText(windowCodePath);

    True(xaml.Contains("x:Name=\"GraphSummaryText\"", StringComparison.Ordinal),
        "Graf powinien pokazywać podsumowanie widocznej mapy.");
    True(xaml.Contains("Click=\"OnFitGraphClick\"", StringComparison.Ordinal),
        "Graf powinien mieć widoczną akcję dopasowania wszystkich elementów.");
    True(xaml.Contains("x:Name=\"GraphSelectionCard\"", StringComparison.Ordinal),
        "Graf powinien opisywać aktualnie zaznaczony element.");
    True(xaml.Contains("x:Name=\"GraphProjectFilter\"", StringComparison.Ordinal),
        "Wybór zakresu projektu powinien być stale widoczny na grafie.");
    True(xaml.Contains("Value=\"#087F75\"", StringComparison.Ordinal),
        "Folder powinien mieć wyraźnie inny kolor niż projekt.");
    True(code.Contains("WikiLinkService.ExtractTitles", StringComparison.Ordinal),
        "Relacje [[wiki]] między widocznymi notatkami powinny trafiać na graf.");
    True(code.Contains("FitToContent", StringComparison.Ordinal),
        "Dopasowanie grafu do okna nie może być atrapą.");
    True(windowCode.Contains("_graphControl.FitToContent", StringComparison.Ordinal),
        "Pierwsze wejście do grafu powinno pokazać elementy zamiast pustego narożnika płótna.");
}

static void GraphProjectScopeIsolated()
{
    var root = new Project { Id = "root", Name = "Atlas", Slug = "atlas" };
    var folder = new Project { Id = "folder", Name = "Dokumentacja", Slug = "docs", ParentId = root.Id, IsFolder = true };
    var nested = new Project { Id = "nested", Name = "API", Slug = "api", ParentId = folder.Id };
    var unrelated = new Project { Id = "other", Name = "Inny", Slug = "inny" };

    var scope = LayoutService.ProjectSubtree([root, folder, nested, unrelated], root.Id);

    Equal(3, scope.Count);
    True(scope.Any(project => project.Id == root.Id));
    True(scope.Any(project => project.Id == folder.Id));
    True(scope.Any(project => project.Id == nested.Id));
    False(scope.Any(project => project.Id == unrelated.Id));
}

static void GraphCardsHaveBreathingRoom()
{
    var project = new Project { Id = "project", Name = "Projekt", Slug = "projekt" };
    var notes = Enumerable.Range(1, 8)
        .Select(index => new Note { Id = $"note-{index}", Title = $"Notatka {index}", Tags = ["projekt"] })
        .ToList();
    var positions = new Dictionary<string, GraphPosition>(StringComparer.OrdinalIgnoreCase);

    LayoutService.ApplyMissingPositions([project], notes, positions);

    var all = positions.Values.ToList();
    for (var first = 0; first < all.Count; first++)
    {
        for (var second = first + 1; second < all.Count; second++)
        {
            var dx = all[first].X - all[second].X;
            var dy = all[first].Y - all[second].Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            True(distance >= 189.9, "Nowe karty grafu nie powinny startować jedna na drugiej.");
        }
    }
}

static void SearchFiltersAndPhrases()
{
    var note = new Note
    {
        Title = "Plan migracji",
        Body = "Pełna fraza znajduje się w treści dokumentu.",
        Tags = ["atlas", "ważne"]
    };

    True(SearchService.Matches("title:plan tag:atlas", note));
    True(SearchService.Matches("body:\"pełna fraza\"", note));
    True(SearchService.Matches("\"plan migracji\"", note));
    False(SearchService.Matches("title:atlas", note), "Filtr pola nie może przeszukiwać pozostałych pól.");
}

static void SearchExclusionsAndTasks()
{
    var note = new Note
    {
        Title = "Spotkanie zespołu",
        Body = "Bez poufnych danych.",
        Checklist = [new ChecklistItem { Text = "Wysłać podsumowanie" }]
    };

    True(SearchService.Matches("spotkanie has:task -body:zakazane", note));
    False(SearchService.Matches("spotkanie -body:poufnych", note));
    False(SearchService.Matches("-has:task", note));
}

static void SearchProjectTypes()
{
    var folder = new Project { Name = "Klienci", Slug = "klienci", IsFolder = true };
    var project = new Project { Name = "Atlas", Slug = "atlas", IsFolder = false };

    True(SearchService.Matches("type:folder", folder));
    False(SearchService.Matches("type:project", folder));
    True(SearchService.Matches("type:project project:atlas", project));

    var system = new Project { Name = "Napęd", ItemType = ProjectItemType.System };
    var product = new Project { Name = "Moduł", ItemType = ProjectItemType.Product };
    var subsystem = new Project { Name = "Kamera", ItemType = ProjectItemType.Subsystem };
    var component = new Project { Name = "PCB", ItemType = ProjectItemType.Component };
    True(SearchService.Matches("type:system", system));
    True(SearchService.Matches("typ:produkt", product));
    True(SearchService.Matches("type:subsystem", subsystem));
    True(SearchService.Matches("typ:komponent", component));
    False(SearchService.Matches("type:product", component));
}

static void ArchitectureTypesAreWired()
{
    var root = FindRepositoryRoot();
    var shell = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "MainWindow.axaml"));
    var graph = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "GraphView.axaml"));

    True(shell.Contains("Text=\"STRUKTURA\"", StringComparison.Ordinal));
    True(shell.Contains("Tag=\"System\"", StringComparison.Ordinal));
    True(shell.Contains("Tag=\"Product\"", StringComparison.Ordinal));
    True(shell.Contains("Tag=\"Subsystem\"", StringComparison.Ordinal));
    True(shell.Contains("Tag=\"Component\"", StringComparison.Ordinal));
    True(shell.Contains("ReflectionBinding TypeColor", StringComparison.Ordinal));
    True(graph.Contains("Border.graph-system", StringComparison.Ordinal));
    True(graph.Contains("Border.graph-product", StringComparison.Ordinal));
    True(graph.Contains("Border.graph-subsystem", StringComparison.Ordinal));
    True(graph.Contains("Border.graph-component", StringComparison.Ordinal));
}

static void NavigationTreeIncludesNotes()
{
    var root = FindRepositoryRoot();
    var shell = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "MainWindow.axaml"));
    var shellCode = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "MainWindow.axaml.cs"));
    var viewModel = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "ViewModels", "MainViewModel.cs"));
    var node = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Models", "NavigationTreeNode.cs"));

    True(shell.Contains("x:Name=\"LibraryTree\"", StringComparison.Ordinal));
    True(shellCode.Contains("node.Note is", StringComparison.Ordinal));
    True(viewModel.Contains("new NavigationTreeNode { Note = note }", StringComparison.Ordinal));
    True(viewModel.Contains("GroupTitle = \"Notatki bez projektu\"", StringComparison.Ordinal));
    True(node.Contains("public Note? Note", StringComparison.Ordinal));
}

static void TaskPriorityDragIsWired()
{
    var root = FindRepositoryRoot();
    var tasks = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "TaskListView.axaml"));
    var tasksCode = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "TaskListView.axaml.cs"));
    var viewModel = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "ViewModels", "MainViewModel.cs"));

    True(tasks.Contains("Przeciągnij kafelek", StringComparison.Ordinal));
    True(tasks.Contains("Cursor=\"SizeAll\"", StringComparison.Ordinal));
    True(tasks.Contains("PointerPressed=\"OnTaskCardPointerPressed\"", StringComparison.Ordinal));
    True(tasksCode.Contains("e.Pointer.Capture(TasksList)", StringComparison.Ordinal));
    True(tasksCode.Contains("GetVisualParent()", StringComparison.Ordinal));
    True(tasksCode.Contains("FindTaskContainerAt", StringComparison.Ordinal));
    True(tasksCode.Contains("ViewModel.MoveTask", StringComparison.Ordinal));
    True(viewModel.Contains("public bool MoveTask", StringComparison.Ordinal));
    True(viewModel.Contains("task.Item.Priority.HasValue", StringComparison.Ordinal));
}

static void PeoplePanelIsWired()
{
    var root = FindRepositoryRoot();
    var shell = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "MainWindow.axaml"));
    var shellCode = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "MainWindow.axaml.cs"));
    var editor = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "EditorPanel.axaml"));
    var people = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "PeopleView.axaml"));

    True(shell.Contains("x:Name=\"PeopleRadio\"", StringComparison.Ordinal));
    True(shell.Contains("x:Name=\"PeopleHost\"", StringComparison.Ordinal));
    True(shellCode.Contains("_vm.PeopleChanged += OnPeopleChanged", StringComparison.Ordinal));
    True(editor.Contains("x:Name=\"ProjectPeopleButton\"", StringComparison.Ordinal));
    True(editor.Contains("x:Name=\"NotePeopleButton\"", StringComparison.Ordinal));
    False(editor.Contains("x:Name=\"ProjectPeopleBox\"", StringComparison.Ordinal));
    False(editor.Contains("x:Name=\"NotePeopleBox\"", StringComparison.Ordinal));
    True(people.Contains("x:Name=\"DetailAvatar\"", StringComparison.Ordinal));
    True(people.Contains("x:Name=\"PersonTasksList\"", StringComparison.Ordinal));
}

static void ClickOutsideDismissesTextEditing()
{
    var root = FindRepositoryRoot();
    var app = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "App.axaml.cs"));
    var behavior = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Services", "FocusDismissService.cs"));

    True(app.Contains("FocusDismissService.Register()", StringComparison.Ordinal));
    True(behavior.Contains("AddClassHandler<TopLevel>", StringComparison.Ordinal));
    True(behavior.Contains("RoutingStrategies.Tunnel", StringComparison.Ordinal));
    True(behavior.Contains("focusManager.Focus(null", StringComparison.Ordinal));
}

static void PersonAssignmentsUseRegistryPicker()
{
    var root = FindRepositoryRoot();
    var editor = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "EditorPanel.axaml"));
    var tasks = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "TaskListView.axaml"));
    var picker = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "PersonPickerMenu.cs"));

    True(editor.Contains("x:Name=\"ProjectPeopleButton\"", StringComparison.Ordinal));
    True(editor.Contains("x:Name=\"NotePeopleButton\"", StringComparison.Ordinal));
    True(tasks.Contains("Click=\"OnTaskPeopleClick\"", StringComparison.Ordinal));
    True(picker.Contains("MenuItemToggleType.CheckBox", StringComparison.Ordinal));
    True(picker.Contains("Brak osób — dodaj je najpierw w panelu Osoby", StringComparison.Ordinal));
    False(editor.Contains("x:Name=\"ProjectPeopleBox\"", StringComparison.Ordinal));
    False(editor.Contains("x:Name=\"NotePeopleBox\"", StringComparison.Ordinal));
}

static void ChecklistPositionRoundTrip()
{
    const string source = "# Spotkanie\n\nPierwszy akapit.\n- [ ] Zadzwonić\nDrugi akapit.";
    var parsed = FrontMatter.Parse(source);
    Equal("Pierwszy akapit.\n- [ ] Zadzwonić\nDrugi akapit.", parsed.Body);
    Equal(1, parsed.Checklist.Count);

    var note = NoteFrom(parsed);
    var reparsed = FrontMatter.Parse(FrontMatter.WriteNote(note));
    Equal("Pierwszy akapit.\n- [ ] Zadzwonić\nDrugi akapit.", reparsed.Body);
    Equal(1, CountOccurrences(reparsed.Body, "- [ ] Zadzwonić"));
}

static void ChecklistToggleInPlace()
{
    var parsed = FrontMatter.Parse("# T\n\nPrzed.\n- [ ] Test\nPo.");
    parsed.Checklist[0].IsDone = true;
    var reparsed = FrontMatter.Parse(FrontMatter.WriteNote(NoteFrom(parsed)));

    Equal("Przed.\n- [x] Test\nPo.", reparsed.Body);
    True(reparsed.Checklist[0].IsDone, "Task powinien być ukończony.");
}

static void ManualBodyEditWins()
{
    var parsed = FrontMatter.Parse("# T\n\nPrzed.\n- [ ] Stara treść\nPo.");
    var note = NoteFrom(parsed);
    note.Body = note.Body.Replace("- [ ] Stara treść", "- [x] Nowa treść", StringComparison.Ordinal);
    var reparsed = FrontMatter.Parse(FrontMatter.WriteNote(note));

    Equal("Przed.\n- [x] Nowa treść\nPo.", reparsed.Body);
    False(reparsed.Body.Contains("Stara treść", StringComparison.Ordinal), "Stary task nie może wrócić.");
    Equal(1, reparsed.Checklist.Count);
}

static void NewPanelTaskIsAppendedOnce()
{
    var note = new Note
    {
        Id = "abc123",
        Title = "Nowa",
        Body = "Akapit.",
        Checklist = [new ChecklistItem { Text = "Nowe zadanie" }]
    };

    FrontMatter.WriteNote(note);
    var secondWrite = FrontMatter.WriteNote(note);
    var reparsed = FrontMatter.Parse(secondWrite);
    Equal(1, CountOccurrences(reparsed.Body, "- [ ] Nowe zadanie"));
    Equal(1, reparsed.Checklist.Count);
}

static void LegacyTaskIsNotDuplicated()
{
    var parsed = FrontMatter.Parse("# T\n\nOpis.\n\n- [ ] Legacy");
    var written = FrontMatter.WriteNote(NoteFrom(parsed));
    var reparsed = FrontMatter.Parse(written);
    Equal(1, CountOccurrences(reparsed.Body, "- [ ] Legacy"));
}

static void ProjectMetadataRoundTrip()
{
    var project = new Project
    {
        Id = "projekt1",
        Name = "Archiwum",
        Slug = "archiwum",
        ParentId = "folder1",
        IsFolder = true,
        IsArchived = true
    };

    var parsed = FrontMatter.Parse(FrontMatter.WriteProject(project));
    Equal("true", parsed["archived"]);
    Equal("folder", parsed["kind"]);
    Equal("folder1", parsed["parent"]);
}

static void ArchitectureTypesRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    store.EnsureFolders();

    var system = store.CreateProject("Sterowanie", null, ProjectItemType.System);
    var product = store.CreateProject("Kontroler", system.Id, ProjectItemType.Product);
    var subsystem = store.CreateProject("Kamera", product.Id, ProjectItemType.Subsystem);
    var component = store.CreateProject("PCB", subsystem.Id, ProjectItemType.Component);

    var loaded = store.LoadProjects().ToDictionary(project => project.Id);
    Equal(ProjectItemType.System, loaded[system.Id].ItemType);
    Equal(ProjectItemType.Product, loaded[product.Id].ItemType);
    Equal(ProjectItemType.Subsystem, loaded[subsystem.Id].ItemType);
    Equal(ProjectItemType.Component, loaded[component.Id].ItemType);
    Equal(system.Id, loaded[product.Id].ParentId);
    Equal(product.Id, loaded[subsystem.Id].ParentId);
    Equal(subsystem.Id, loaded[component.Id].ParentId);
    Equal("system", FrontMatter.Parse(FrontMatter.WriteProject(system))["kind"]);
    Equal(ProjectItemType.Project, ProjectItemTypeCatalog.Parse(null));
}

static void ArchitectureHierarchyRules()
{
    True(ProjectItemType.System.CanContain(ProjectItemType.Product));
    False(ProjectItemType.System.CanContain(ProjectItemType.Component));
    True(ProjectItemType.Product.CanContain(ProjectItemType.Subsystem));
    True(ProjectItemType.Product.CanContain(ProjectItemType.Component));
    False(ProjectItemType.Product.CanContain(ProjectItemType.Product));
    True(ProjectItemType.Subsystem.CanContain(ProjectItemType.Component));
    False(ProjectItemType.Component.CanContain(ProjectItemType.Component));
    True(ProjectItemType.Folder.CanContain(ProjectItemType.System));
    True(ProjectItemType.Project.CanContain(ProjectItemType.Component));
}

static void PersonAssignmentsRoundTrip()
{
    var note = new Note
    {
        Id = "note-people",
        Title = "Spotkanie",
        Tags = ["projekt-x"],
        People = ["anna-kowalska", "piotr-nowak"]
    };
    var parsedNote = FrontMatter.Parse(FrontMatter.WriteNote(note));
    Equal("projekt-x", parsedNote["tags"]);
    Equal("anna-kowalska, piotr-nowak", parsedNote["people"]);

    var project = new Project
    {
        Id = "project-people",
        Name = "Wdrożenie",
        Slug = "wdrozenie",
        People = ["anna-kowalska"]
    };
    var parsedProject = FrontMatter.Parse(FrontMatter.WriteProject(project));
    Equal("anna-kowalska", parsedProject["people"]);
}

static void PersonNameResolvesToStableSlug()
{
    var people = new[]
    {
        new Person { Name = "Anna Kowalska", Slug = "nowa-osoba" },
        new Person { Name = "Piotr Nowak", Slug = "piotr-nowak" }
    };

    var resolved = PersonTagService.Resolve("Anna Kowalska, @piotr-nowak, osoba-spoza-rejestru", people);
    Equal(2, resolved.Count);
    Equal("nowa-osoba", resolved[0]);
    Equal("piotr-nowak", resolved[1]);
    False(resolved.Contains("osoba-spoza-rejestru"), "Nie można przypisać osoby spoza rejestru.");

    var sanitized = PersonTagService.KeepRegistered(["nowa-osoba", "nieistniejaca-osoba"], people);
    Equal(1, sanitized.Count);
    Equal("nowa-osoba", sanitized[0]);
}

static void TaskPeopleRoundTrip()
{
    const string source = "# Plan\n\n- [ ] Ustalić zakres <!-- people: Anna Kowalska, piotr-nowak -->";
    var parsed = FrontMatter.Parse(source);
    Equal(1, parsed.Checklist.Count);
    Equal("Ustalić zakres", parsed.Checklist[0].Text);
    Equal("anna-kowalska", parsed.Checklist[0].People[0]);
    Equal("piotr-nowak", parsed.Checklist[0].People[1]);

    parsed.Checklist[0].People = ["ewa-lis"];
    var rewritten = FrontMatter.WriteNote(NoteFrom(parsed));
    True(rewritten.Contains("<!-- people: ewa-lis -->", StringComparison.Ordinal));
    False(WikiLinkService.ToPreviewLines(FrontMatter.Parse(rewritten).Body)
        .Any(line => line.Text.Contains("people:", StringComparison.OrdinalIgnoreCase)));

    var blocks = VisualDocumentService.Parse(FrontMatter.Parse(rewritten).Body);
    Equal("ewa-lis", blocks.Single(block => block.Kind == DocumentBlockKind.Checklist).People.Single());
    True(VisualDocumentService.Serialize(blocks).Contains("<!-- people: ewa-lis -->", StringComparison.Ordinal));
}

static void TaskPriorityRoundTrip()
{
    const string source = "# Plan\n\n- [ ] Pilne <!-- priority: 100 --> <!-- people: anna -->";
    var parsed = FrontMatter.Parse(source);
    var item = parsed.Checklist.Single();
    Equal("Pilne", item.Text);
    Equal(100, item.Priority);
    Equal("anna", item.People.Single());

    item.Priority = 250;
    var rewritten = FrontMatter.WriteNote(NoteFrom(parsed));
    True(rewritten.Contains("<!-- priority: 250 --> <!-- people: anna -->", StringComparison.Ordinal));

    var block = VisualDocumentService.Parse(FrontMatter.Parse(rewritten).Body).Single();
    Equal(250, block.TaskPriority);
    Equal("Pilne", block.Text);
    True(VisualDocumentService.Serialize([block]).Contains("<!-- priority: 250 -->", StringComparison.Ordinal));
}

static void PersonStoreRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var person = store.CreatePerson("Anna Kowalska");
    person.Role = "Klientka";
    person.Description = "Prowadzi wdrożenie.";
    person.AvatarPath = $"Assets/{person.Id}/avatar.png";
    store.SavePerson(person);

    var loaded = store.LoadPeople().Single();
    Equal("Anna Kowalska", loaded.Name);
    Equal("anna-kowalska", loaded.Slug);
    Equal("Klientka", loaded.Role);
    Equal("Prowadzi wdrożenie.", loaded.Description);
    Equal(person.AvatarPath, loaded.AvatarPath);
    True(File.Exists(Path.Combine(store.PeopleFolder, "anna-kowalska.md")));
}

static void AtomicSaveCreatesBackup()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var note = store.CreateNote("Atomowy");
    note.Body = "wersja pierwsza";
    store.SaveNote(note);
    var firstVersion = File.ReadAllText(note.FilePath);

    note.Body = "wersja druga";
    store.SaveNote(note);

    var backupPath = note.FilePath + ".bak";
    True(File.Exists(backupPath), "Brakuje kopii .bak.");
    Equal(firstVersion, File.ReadAllText(backupPath));
    True(File.ReadAllText(note.FilePath).Contains("wersja druga", StringComparison.Ordinal));
    Equal(0, Directory.GetFiles(store.NotesFolder, "*.tmp", SearchOption.TopDirectoryOnly).Length);
}

static void FailedRenamePreservesOriginal()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var note = store.CreateNote("Stara nazwa");
    note.Body = "ważna treść";
    store.SaveNote(note);
    var originalPath = note.FilePath;
    var originalText = File.ReadAllText(originalPath);

    note.Title = "Zablokowana";
    var blockedPath = Path.Combine(store.NotesFolder, $"zablokowana-{note.Id}.md");
    Directory.CreateDirectory(blockedPath);
    Throws<IOException>(() => store.SaveNote(note));

    True(File.Exists(originalPath), "Stary plik musi pozostać po błędzie zapisu.");
    Equal(originalText, File.ReadAllText(originalPath));
    Equal(originalPath, note.FilePath);
}

static void FailedRenameArchiveRollsBack()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var note = store.CreateNote("Stara");
    var originalPath = note.FilePath;
    var originalText = File.ReadAllText(originalPath);
    Directory.CreateDirectory(originalPath + ".bak");

    note.Title = "Nowa";
    var newPath = Path.Combine(store.NotesFolder, $"nowa-{note.Id}.md");
    Throws<IOException>(() => store.SaveNote(note));

    True(File.Exists(originalPath), "Stary plik musi pozostać po błędzie archiwizacji.");
    Equal(originalText, File.ReadAllText(originalPath));
    False(File.Exists(newPath), "Nowy plik powinien zostać wycofany.");
    Equal(originalPath, note.FilePath);
}

static void ProjectSlugCannotEscapeRoot()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var project = store.CreateProject("Bezpieczny");
    var originalPath = project.FilePath;
    project.Slug = "../../poza-root";

    Throws<InvalidDataException>(() => store.SaveProject(project));
    True(File.Exists(originalPath), "Walidacja sluga nie może usunąć starego pliku.");
    False(File.Exists(Path.Combine(temp.Path, "poza-root.md")));
}

static void ExternalFilePathIsRejected()
{
    using var temp = new TemporaryDirectory();
    var outside = Path.Combine(temp.Path, "poza.md");
    File.WriteAllText(outside, "nie usuwać");
    var store = new MarkdownStore(Path.Combine(temp.Path, "library"));
    var note = new Note { Id = "abc123", Title = "Test", Body = "x", FilePath = outside };

    Throws<InvalidDataException>(() => store.SaveNote(note));
    Equal("nie usuwać", File.ReadAllText(outside));
}

static void FileSystemRootContainmentWorks()
{
    var root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))
        ?? throw new InvalidOperationException("Brak katalogu głównego systemu plików.");
    var fileName = $"mapa-notatek-{Guid.NewGuid():N}.md";
    var expected = Path.GetFullPath(Path.Combine(root, fileName));

    Equal(expected, SafeFileStorage.GetContainedFilePath(root, fileName));
}

static void ReadFailureIsReported()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var badPath = Path.Combine(store.NotesFolder, "broken.md");
    File.WriteAllText(badPath, "---\nid: abc123\ntype: note\n# bez końca");
    var raised = new List<StorageReadIssue>();
    store.ReadIssueDetected += raised.Add;

    var notes = store.LoadNotes();
    Equal(0, notes.Count);
    Equal(1, store.ReadIssues.Count);
    Equal(1, raised.Count);
    Equal(badPath, store.ReadIssues[0].Path);
    False(store.ReadIssues[0].RecoveredFromBackup);
}

static void NoteRecoversFromBackup()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var note = store.CreateNote("Odzysk");
    note.Body = "kopia";
    store.SaveNote(note);
    note.Body = "najnowsza";
    store.SaveNote(note);
    File.WriteAllText(note.FilePath, "---\nuszkodzony");

    var loaded = store.LoadNotes();
    Equal(1, loaded.Count);
    Equal("kopia", loaded[0].Body);
    True(store.ReadIssues.Single().RecoveredFromBackup);
}

static void AppStateRecoversFromBackup()
{
    using var temp = new TemporaryDirectory();
    var store = new AppStateStore(temp.Path);
    store.Save(new AppState { DataFolder = temp.Path, Zoom = 1.25 });
    store.Save(new AppState { DataFolder = temp.Path, Zoom = 2.0 });
    File.WriteAllText(store.StatePath, "{ broken");

    var loaded = store.Load();
    Equal(1.25, loaded.Zoom);
    True(store.LastLoadIssue?.RecoveredFromBackup == true);
}

static void EditorAppearanceRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var store = new AppStateStore(temp.Path);
    store.Save(new AppState
    {
        DataFolder = temp.Path,
        EditorFont = "Monospace",
        EditorFontSize = 18
    });

    var loaded = store.Load();
    Equal("Monospace", loaded.EditorFont);
    Equal(18d, loaded.EditorFontSize);
}

static void SidePanelVisibilityRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var store = new AppStateStore(temp.Path);
    store.Save(new AppState
    {
        DataFolder = temp.Path,
        SidebarVisible = false,
        EditorDetailsVisible = false
    });

    var loaded = store.Load();
    False(loaded.SidebarVisible);
    False(loaded.EditorDetailsVisible);

    var root = FindRepositoryRoot();
    var window = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "MainWindow.axaml"));
    var windowCode = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "MainWindow.axaml.cs"));
    var editorCode = File.ReadAllText(Path.Combine(root, "src", "MapaNotatek", "Views", "EditorPanel.axaml.cs"));
    True(window.Contains("x:Name=\"SidebarToggleButton\"", StringComparison.Ordinal));
    True(window.Contains("Click=\"OnToggleEditorDetails\"", StringComparison.Ordinal));
    True(windowCode.Contains("ApplyShellVisibility", StringComparison.Ordinal));
    True(editorCode.Contains("SaveEditorDetailsVisibility", StringComparison.Ordinal));
}

static void CorruptStateWithoutBackupThrows()
{
    using var temp = new TemporaryDirectory();
    var store = new AppStateStore(temp.Path);
    Directory.CreateDirectory(temp.Path);
    File.WriteAllText(store.StatePath, "{ broken");

    Throws<StorageReadException>(() => store.Load());
    True(store.LastLoadIssue is { RecoveredFromBackup: false });
}

static void RecoverStatePreservesBrokenFile()
{
    using var temp = new TemporaryDirectory();
    var store = new AppStateStore(temp.Path);
    Directory.CreateDirectory(temp.Path);
    File.WriteAllText(store.StatePath, "{ broken");
    Throws<StorageReadException>(() => store.Load());

    var recovered = store.RecoverWithFreshState();
    Equal(temp.Path, recovered.DataFolder);
    True(File.Exists(store.StatePath));
    Equal(1, Directory.GetFiles(Path.Combine(temp.Path, "Recovery"), "*.corrupt").Length);
    Equal(temp.Path, store.Load().DataFolder);
}

static void UnterminatedFrontMatterThrows()
{
    Throws<FormatException>(() => FrontMatter.Parse("---\nid: abc123\n# Tytuł"));
}

static void BackupContainsOnlyApplicationData()
{
    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "source");
    var store = new MarkdownStore(source);
    var note = store.CreateNote("Kopia");
    var person = store.CreatePerson("Anna Kowalska");
    note.Body = "ważna treść";
    store.SaveNote(note);
    var assetFolder = Path.Combine(source, "Assets", note.Id);
    Directory.CreateDirectory(assetFolder);
    File.WriteAllBytes(Path.Combine(assetFolder, "obraz.png"), MinimalPngBytes());
    File.WriteAllText(Path.Combine(source, "obcy-sekret.txt"), "nie kopiować");

    var backup = Path.Combine(temp.Path, "backup");
    BackupService.ExportCopy(source, backup);

    var validation = BackupService.Validate(backup);
    True(validation.IsValid, string.Join("; ", validation.Errors));
    True(File.Exists(Path.Combine(backup, "Notes", Path.GetFileName(note.FilePath))));
    True(File.Exists(Path.Combine(backup, "People", Path.GetFileName(person.FilePath))));
    True(File.Exists(Path.Combine(backup, "Assets", note.Id, "obraz.png")));
    False(File.Exists(Path.Combine(backup, "obcy-sekret.txt")), "Kopia nie może zabierać obcych plików.");
    True(validation.Manifest?.Files.Count >= 2);
}

static void BackupDetectsTampering()
{
    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "source");
    var store = new MarkdownStore(source);
    var note = store.CreateNote("Integralność");
    var backup = Path.Combine(temp.Path, "backup");
    BackupService.ExportCopy(source, backup);

    File.AppendAllText(Path.Combine(backup, "Notes", Path.GetFileName(note.FilePath)), "\nzmiana");
    var validation = BackupService.Validate(backup);
    False(validation.IsValid);
    True(validation.Errors.Any(error => error.Contains("rozmiar", StringComparison.OrdinalIgnoreCase) ||
                                        error.Contains("kontrol", StringComparison.OrdinalIgnoreCase)));
}

static void BackupRestoresToEmptyFolder()
{
    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "source");
    var store = new MarkdownStore(source);
    var note = store.CreateNote("Odtwórz");
    note.Body = "treść do odzyskania";
    store.SaveNote(note);
    var stateStore = new AppStateStore(source);
    var state = new AppState
    {
        DataFolder = source,
        Zoom = 1.75,
        FocusedProjectId = "projekt-1",
        PinnedIds = [note.Id],
        RecentIds = [note.Id],
        NodePositions = new Dictionary<string, GraphPosition>
        {
            [note.Id] = new() { Id = note.Id, X = 321, Y = 654 }
        }
    };
    stateStore.Save(state);
    var backup = Path.Combine(temp.Path, "backup");
    BackupService.ExportCopy(source, backup);

    var restored = Path.Combine(temp.Path, "restored");
    Directory.CreateDirectory(restored);
    BackupService.RestoreCopy(backup, restored);
    var restoredStore = new MarkdownStore(restored);
    Equal("treść do odzyskania", restoredStore.LoadNotes().Single().Body);
    Equal(LibraryFolderKind.ExistingLibrary, LibraryFolderService.Inspect(restored).Kind);
    var restoredState = new AppStateStore(restored).Load();
    Equal(1.75, restoredState.Zoom);
    Equal(note.Id, restoredState.PinnedIds.Single());
    Equal(321d, restoredState.NodePositions[note.Id].X);

    File.WriteAllText(Path.Combine(restored, "occupied.txt"), "x");
    Throws<IOException>(() => BackupService.RestoreCopy(backup, restored));
}

static void BackupCannotBeInsideLibrary()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    store.CreateNote("Test");
    Throws<IOException>(() => BackupService.ExportCopy(temp.Path, Path.Combine(temp.Path, "Backup")));
}

static void BackupRestoreCannotTargetInsideBackup()
{
    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "source");
    var store = new MarkdownStore(source);
    store.CreateNote("Test");
    var backup = Path.Combine(temp.Path, "backup");
    BackupService.ExportCopy(source, backup);

    Throws<IOException>(() => BackupService.RestoreCopy(backup, Path.Combine(backup, "restored")));
}

static void BackupRejectsSymbolicLinks()
{
    if (OperatingSystem.IsWindows())
    {
        return;
    }

    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "source");
    var store = new MarkdownStore(source);
    var note = store.CreateNote("Dowiązanie");
    var backup = Path.Combine(temp.Path, "backup");
    BackupService.ExportCopy(source, backup);

    var backedUpNote = Path.Combine(backup, "Notes", Path.GetFileName(note.FilePath));
    var outside = Path.Combine(temp.Path, "outside.md");
    File.WriteAllText(outside, File.ReadAllText(backedUpNote));
    File.Delete(backedUpNote);
    File.CreateSymbolicLink(backedUpNote, outside);

    var validation = BackupService.Validate(backup);
    False(validation.IsValid);
    True(validation.Errors.Any(error => error.Contains("dowiąz", StringComparison.OrdinalIgnoreCase)));
}

static void AttachmentIsImportedLocally()
{
    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "wejście.png");
    File.WriteAllBytes(source, MinimalPngBytes());
    var library = Path.Combine(temp.Path, "library");
    var attachments = new AttachmentStore(library);

    var imported = attachments.ImportImage("note123", source);
    True(File.Exists(imported.FullPath));
    True(imported.FullPath.StartsWith(Path.Combine(library, "Assets", "note123") + Path.DirectorySeparatorChar));
    True(imported.MarkdownPath.StartsWith("../Assets/note123/", StringComparison.Ordinal));
    Equal("image/png", imported.MimeType);
    Equal(1, attachments.ListNoteAssets("note123").Count);
}

static void InvalidAttachmentIsRejected()
{
    using var temp = new TemporaryDirectory();
    var fake = Path.Combine(temp.Path, "fałszywy.png");
    File.WriteAllText(fake, "to nie jest obraz");
    var attachments = new AttachmentStore(Path.Combine(temp.Path, "library"));

    Throws<InvalidDataException>(() => attachments.ImportImage("note123", fake));
    Throws<InvalidDataException>(() => attachments.ImportImage("../../outside", fake));
}

static void RevisionHistoryIsBounded()
{
    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "current.md");
    var history = new RevisionStore(temp.Path);
    File.WriteAllText(source, "wersja 0");
    history.CaptureExisting("Notes", "note123", source, retention: 3);
    history.CaptureExisting("Notes", "note123", source, retention: 3);
    Equal(1, history.List("Notes", "note123").Count);

    for (var index = 1; index <= 5; index++)
    {
        File.WriteAllText(source, "wersja " + index);
        history.CaptureExisting("Notes", "note123", source, retention: 3);
    }

    var revisions = history.List("Notes", "note123");
    Equal(3, revisions.Count);
    True(revisions.All(revision => history.Read(revision).StartsWith("wersja ", StringComparison.Ordinal)));
}

static void UnrelatedFolderIsRejected()
{
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "rodzinne-zdjęcia.txt"), "dane");
    Equal(LibraryFolderKind.UnrelatedContent, LibraryFolderService.Inspect(temp.Path).Kind);
    Throws<InvalidOperationException>(() => LibraryFolderService.EnsureCanOpen(temp.Path));
}

static void OfflineSourcePolicy()
{
    var root = FindRepositoryRoot();
    var banned = new[]
    {
        "HttpClient",
        "WebRequest",
        "WebClient",
        "ClientWebSocket",
        "System.Net.Sockets",
        "TcpClient",
        "UdpClient",
        "NativeWebView"
    };
    var sourceFiles = Directory.EnumerateFiles(Path.Combine(root, "src", "MapaNotatek"), "*.cs", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "MapaNotatek"), "*.csproj", SearchOption.TopDirectoryOnly));
    foreach (var path in sourceFiles)
    {
        var content = File.ReadAllText(path);
        foreach (var token in banned)
        {
            False(content.Contains(token, StringComparison.Ordinal), $"Niedozwolony klient sieciowy „{token}” w {path}.");
        }
    }
}

static void ProjectTrashRoundTrip()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var project = store.CreateProject("Ważny projekt");
    project.Description = "treść projektu";
    store.SaveProject(project);
    var originalPath = project.FilePath;

    store.MoveProjectToTrash(project);
    False(File.Exists(originalPath));
    True(project.FilePath.StartsWith(store.TrashedProjectsFolder + Path.DirectorySeparatorChar));
    Equal(1, store.LoadTrashedProjects().Count);

    store.RestoreProjectFromTrash(project);
    True(File.Exists(project.FilePath));
    True(project.FilePath.StartsWith(store.ProjectsFolder + Path.DirectorySeparatorChar));
    Equal("treść projektu", store.LoadProjects().Single().Description);
}

static void ProjectTrashCollisionPersistsUniqueSlug()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var trashed = store.CreateProject("Ten sam projekt");
    store.MoveProjectToTrash(trashed);
    var active = store.CreateProject("Ten sam projekt");

    store.RestoreProjectFromTrash(trashed);
    var loaded = store.LoadProjects();
    Equal(2, loaded.Count);
    Equal(2, loaded.Select(project => project.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    var restored = loaded.Single(project => project.Id == trashed.Id);
    Equal(trashed.Slug, restored.Slug);
    False(string.Equals(active.Slug, restored.Slug, StringComparison.OrdinalIgnoreCase));

    restored.Description = "można dalej zapisywać";
    store.SaveProject(restored);
    Equal("można dalej zapisywać", store.LoadProjects().Single(project => project.Id == restored.Id).Description);
}

static void NoteTrashMovesBackup()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var note = store.CreateNote("Kopia w koszu");
    note.Body = "pierwsza";
    store.SaveNote(note);
    note.Body = "druga";
    store.SaveNote(note);
    var originalPath = note.FilePath;
    var originalBackup = originalPath + ".bak";
    True(File.Exists(originalBackup));

    store.MoveNoteToTrash(note);
    False(File.Exists(originalBackup));
    True(File.Exists(note.FilePath + ".bak"));

    store.RestoreNoteFromTrash(note);
    True(File.Exists(note.FilePath));
    True(File.Exists(note.FilePath + ".bak"));
}

static void TrashedProjectRecoversFromBackup()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var project = store.CreateProject("Odzyskanie z kosza");
    project.Description = "wersja zapasowa";
    store.SaveProject(project);
    project.Description = "wersja główna";
    store.SaveProject(project);
    store.MoveProjectToTrash(project);
    File.WriteAllText(project.FilePath, "---\nid: broken");

    var recovered = store.LoadTrashedProjects().Single();
    Equal("wersja zapasowa", recovered.Description);
    True(store.ReadIssues.Any(issue => issue.Area == StorageArea.Trash && issue.RecoveredFromBackup));
}

static void ExternalModificationCreatesConflict()
{
    using var temp = new TemporaryDirectory();
    var store = new MarkdownStore(temp.Path);
    var note = store.CreateNote("Konflikt");
    note.Body = "wersja aplikacji";
    store.SaveNote(note);
    File.AppendAllText(note.FilePath, "\nzmiana zewnętrzna");
    var externalContent = File.ReadAllText(note.FilePath);

    note.Body = "nowsza wersja w pamięci";
    Throws<StorageConflictException>(() => store.SaveNote(note));
    Equal(externalContent, File.ReadAllText(note.FilePath));
}

static void AtomicReplaceRestoresRacedFile()
{
    using var temp = new TemporaryDirectory();
    var target = Path.Combine(temp.Path, "note.md");
    File.WriteAllText(target, "zmiana zewnętrzna");
    var staleHash = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("stara wersja")));

    Throws<StorageConflictException>(() =>
        SafeFileStorage.AtomicWriteAllText(target, "draft aplikacji", staleHash));
    Equal("zmiana zewnętrzna", File.ReadAllText(target));
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "MapaNotatek.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Nie znaleziono katalogu repozytorium.");
}

static byte[] MinimalPngBytes() =>
[
    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    0x00, 0x00, 0x00, 0x0D
];

static Note NoteFrom(FrontMatter.ParsedDocument parsed) => new()
{
    Id = "abc123",
    Title = parsed.Title,
    Body = parsed.Body,
    Checklist = parsed.Checklist
};

static int CountOccurrences(string text, string value)
{
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += value.Length;
    }

    return count;
}

static void True(bool condition, string? message = null)
{
    if (!condition)
    {
        throw new InvalidOperationException(message ?? "Oczekiwano true.");
    }
}

static void False(bool condition, string? message = null) => True(!condition, message ?? "Oczekiwano false.");

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Oczekiwano: {expected}; otrzymano: {actual}");
    }
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Oczekiwano wyjątku {typeof(TException).Name}.");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"MapaNotatek-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
