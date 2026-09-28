using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MapaNotatek.Models;
using MapaNotatek.Services;
using MapaNotatek.ViewModels;

namespace MapaNotatek.Views;

public partial class PeopleView : UserControl
{
    private const double NodeWidth = 132;
    private const double NodeHeight = 142;
    private bool _suppress;
    private bool _canvasCentered;
    private string? _selectedPersonId;
    private readonly Dictionary<string, TextBlock> _nodeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _nodeRoles = new(StringComparer.OrdinalIgnoreCase);

    public PeopleView()
    {
        InitializeComponent();
    }

    public MainViewModel? ViewModel { get; set; }

    public event Action<Project>? ProjectOpenRequested;
    public event Action<Note>? NoteOpenRequested;

    public void Refresh()
    {
        if (ViewModel is null)
        {
            return;
        }

        var people = FilteredPeople().ToList();
        PeopleCountText.Text = people.Count == 1 ? "1 osoba" : $"{people.Count} osób";
        PeopleEmptyState.IsVisible = people.Count == 0;
        PeopleCanvas.IsVisible = people.Count > 0;

        if (_selectedPersonId is not null && ViewModel.People.All(person => person.Id != _selectedPersonId))
        {
            _selectedPersonId = null;
        }

        RenderPeopleGraph(people);
        PopulateDetails();
        if (people.Count > 0 && !_canvasCentered)
        {
            _canvasCentered = true;
            Dispatcher.UIThread.Post(CenterCanvas, DispatcherPriority.Background);
        }
    }

    public void CreatePerson()
    {
        if (ViewModel is null)
        {
            return;
        }

        var person = ViewModel.NewPerson();
        _selectedPersonId = person.Id;
        Refresh();
        Dispatcher.UIThread.Post(() =>
        {
            PersonNameBox.Focus();
            PersonNameBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.Source, this))
        {
            PeopleScroll.Focus();
        }
    }

    private IEnumerable<Person> FilteredPeople()
    {
        if (ViewModel is null)
        {
            return [];
        }

        var query = ViewModel.SearchQuery.Trim();
        return ViewModel.People
            .Where(person => string.IsNullOrWhiteSpace(query) ||
                             person.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                             person.Slug.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             person.Role.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                             person.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private void RenderPeopleGraph(IReadOnlyList<Person> people)
    {
        PeopleCanvas.Children.Clear();
        _nodeNames.Clear();
        _nodeRoles.Clear();
        AddGrid();
        if (people.Count == 0)
        {
            return;
        }

        var positions = LayoutPositions(people.Count);
        for (var left = 0; left < people.Count; left++)
        {
            for (var right = left + 1; right < people.Count; right++)
            {
                var strength = SharedContextCount(people[left], people[right]);
                if (strength == 0)
                {
                    continue;
                }

                var from = positions[left];
                var to = positions[right];
                PeopleCanvas.Children.Add(new Line
                {
                    StartPoint = new Point(from.X + (NodeWidth / 2), from.Y + 54),
                    EndPoint = new Point(to.X + (NodeWidth / 2), to.Y + 54),
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)Math.Min(135, 52 + (strength * 20)), 72, 118, 190)),
                    StrokeThickness = Math.Min(3.2, 1.2 + (strength * 0.35)),
                    IsHitTestVisible = false
                });
            }
        }

        for (var index = 0; index < people.Count; index++)
        {
            AddPersonNode(people[index], positions[index]);
        }
    }

    private void AddGrid()
    {
        for (var coordinate = 80; coordinate < PeopleCanvas.Width; coordinate += 120)
        {
            PeopleCanvas.Children.Add(new Line
            {
                StartPoint = new Point(coordinate, 0),
                EndPoint = new Point(coordinate, PeopleCanvas.Height),
                Stroke = new SolidColorBrush(Color.FromArgb(10, 80, 120, 180)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
        }

        for (var coordinate = 80; coordinate < PeopleCanvas.Height; coordinate += 120)
        {
            PeopleCanvas.Children.Add(new Line
            {
                StartPoint = new Point(0, coordinate),
                EndPoint = new Point(PeopleCanvas.Width, coordinate),
                Stroke = new SolidColorBrush(Color.FromArgb(10, 80, 120, 180)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
        }
    }

    private void CenterCanvas()
    {
        var viewport = PeopleScroll.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            _canvasCentered = false;
            return;
        }

        PeopleScroll.Offset = new Vector(
            Math.Max(0, (PeopleCanvas.Width - viewport.Width) / 2),
            Math.Max(0, (PeopleCanvas.Height - viewport.Height) / 2));
    }

    private static List<Point> LayoutPositions(int count)
    {
        var result = new List<Point>(count);
        var center = new Point(800, 550);
        if (count == 1)
        {
            result.Add(new Point(center.X - (NodeWidth / 2), center.Y - 54));
            return result;
        }

        var remaining = count;
        var index = 0;
        var ring = 0;
        while (remaining > 0)
        {
            var capacity = Math.Min(remaining, 8 + (ring * 5));
            var radiusX = 245 + (ring * 205);
            var radiusY = 185 + (ring * 150);
            for (var item = 0; item < capacity; item++)
            {
                var angle = (-Math.PI / 2) + ((Math.PI * 2 * item) / capacity) + (ring * 0.18);
                result.Add(new Point(
                    center.X + (Math.Cos(angle) * radiusX) - (NodeWidth / 2),
                    center.Y + (Math.Sin(angle) * radiusY) - 54));
                index++;
            }

            remaining -= capacity;
            ring++;
        }

        return result;
    }

    private int SharedContextCount(Person left, Person right)
    {
        if (ViewModel is null)
        {
            return 0;
        }

        var projects = ViewModel.Projects.Count(project =>
            PersonTagService.Contains(project.People, left.Slug) &&
            PersonTagService.Contains(project.People, right.Slug));
        var notes = ViewModel.Notes.Count(note =>
            PersonTagService.Contains(note.People, left.Slug) &&
            PersonTagService.Contains(note.People, right.Slug));
        var tasks = ViewModel.Projects.SelectMany(project => project.Checklist)
            .Concat(ViewModel.Notes.SelectMany(note => note.Checklist))
            .Count(task => PersonTagService.Contains(task.People, left.Slug) &&
                           PersonTagService.Contains(task.People, right.Slug));
        return projects + notes + tasks;
    }

    private void AddPersonNode(Person person, Point position)
    {
        var selected = string.Equals(_selectedPersonId, person.Id, StringComparison.OrdinalIgnoreCase);
        var name = new TextBlock
        {
            Text = person.Name,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 116
        };
        var role = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(person.Role) ? "bez roli" : person.Role,
            FontSize = 10.5,
            Opacity = 0.62,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 116
        };
        _nodeNames[person.Id] = name;
        _nodeRoles[person.Id] = role;

        var button = new Button
        {
            Width = NodeWidth,
            Height = NodeHeight,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(16),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = selected ? ResourceBrush("AccentBrush", Brushes.DodgerBlue) : ResourceBrush("BorderSubtleBrush", Brushes.Gray),
            BorderThickness = new Thickness(selected ? 2.5 : 1),
            Content = new StackPanel
            {
                Spacing = 7,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { CreateAvatar(person, 76), name, role }
            }
        };
        ToolTip.SetTip(button, $"{person.Name}\n{InvolvementSummary(person)}");
        button.Click += (_, _) =>
        {
            _selectedPersonId = person.Id;
            RenderPeopleGraph(FilteredPeople().ToList());
            PopulateDetails();
        };
        Canvas.SetLeft(button, position.X);
        Canvas.SetTop(button, position.Y);
        PeopleCanvas.Children.Add(button);
    }

    private Control CreateAvatar(Person person, double size)
    {
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            ClipToBounds = true,
            Background = AvatarBrush(person.Slug)
        };
        var path = ResolveAvatarPath(person);
        if (path is not null)
        {
            try
            {
                border.Child = new Image
                {
                    Source = new Bitmap(path),
                    Stretch = Stretch.UniformToFill,
                    Width = size,
                    Height = size
                };
                return border;
            }
            catch
            {
                // A missing or damaged avatar falls back to initials.
            }
        }

        border.Child = new TextBlock
        {
            Text = Initials(person.Name),
            FontSize = size * 0.32,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return border;
    }

    private void PopulateDetails()
    {
        if (ViewModel is null)
        {
            return;
        }

        var person = ViewModel.People.FirstOrDefault(candidate => candidate.Id == _selectedPersonId);
        PersonEmptyDetail.IsVisible = person is null;
        PersonDetail.IsVisible = person is not null;
        if (person is null)
        {
            return;
        }

        _suppress = true;
        DetailAvatar.Content = CreateAvatar(person, 104);
        PersonNameBox.Text = person.Name;
        PersonRoleBox.Text = person.Role;
        PersonDescriptionBox.Text = person.Description;
        PersonSlugText.Text = $"Identyfikator przypisań: {person.Slug}";
        RemoveAvatarButton.IsVisible = !string.IsNullOrWhiteSpace(person.AvatarPath);

        var projects = ViewModel.Projects
            .Where(project => PersonTagService.Contains(project.People, person.Slug))
            .Select(project => new PersonRelatedItem(
                project.Name,
                project.ItemType.Label(),
                project.Id,
                IsProject: true))
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var notes = ViewModel.Notes
            .Where(note => PersonTagService.Contains(note.People, person.Slug))
            .Select(note => new PersonRelatedItem(
                note.Title,
                note.Tags.Count == 0 ? "Notatka" : "Notatka · " + string.Join(", ", note.Tags.Take(3)),
                note.Id,
                IsProject: false))
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var tasks = BuildTaskRelations(person).ToList();

        PersonProjectsList.ItemsSource = projects;
        PersonNotesList.ItemsSource = notes;
        PersonTasksList.ItemsSource = tasks;
        PersonProjectsList.IsVisible = projects.Count > 0;
        PersonNotesList.IsVisible = notes.Count > 0;
        PersonTasksList.IsVisible = tasks.Count > 0;
        ProjectsEmptyText.IsVisible = projects.Count == 0;
        NotesEmptyText.IsVisible = notes.Count == 0;
        TasksEmptyText.IsVisible = tasks.Count == 0;
        PersonProjectsCount.Text = projects.Count.ToString();
        PersonNotesCount.Text = notes.Count.ToString();
        PersonTasksCount.Text = tasks.Count(item => !item.IsDone).ToString();
        _suppress = false;
    }

    private IEnumerable<PersonRelatedItem> BuildTaskRelations(Person person)
    {
        if (ViewModel is null)
        {
            yield break;
        }

        foreach (var project in ViewModel.Projects)
        {
            foreach (var task in project.Checklist.Where(task => PersonTagService.Contains(task.People, person.Slug)))
            {
                yield return new PersonRelatedItem(
                    (task.IsDone ? "✓ " : "○ ") + task.Text,
                    project.ItemType.Label() + " · " + project.Name,
                    project.Id,
                    IsProject: true,
                    task.IsDone);
            }
        }

        foreach (var note in ViewModel.Notes)
        {
            foreach (var task in note.Checklist.Where(task => PersonTagService.Contains(task.People, person.Slug)))
            {
                yield return new PersonRelatedItem(
                    (task.IsDone ? "✓ " : "○ ") + task.Text,
                    "Notatka · " + note.Title,
                    note.Id,
                    IsProject: false,
                    task.IsDone);
            }
        }
    }

    private void OnNewPerson(object? sender, RoutedEventArgs e) => CreatePerson();

    private void OnPersonChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || ViewModel?.People.FirstOrDefault(candidate => candidate.Id == _selectedPersonId) is not { } person)
        {
            return;
        }

        person.Name = string.IsNullOrWhiteSpace(PersonNameBox.Text) ? "Bez imienia" : PersonNameBox.Text.Trim();
        person.Role = (PersonRoleBox.Text ?? string.Empty).Trim();
        person.Description = PersonDescriptionBox.Text ?? string.Empty;
        if (_nodeNames.TryGetValue(person.Id, out var name))
        {
            name.Text = person.Name;
        }

        if (_nodeRoles.TryGetValue(person.Id, out var role))
        {
            role.Text = string.IsNullOrWhiteSpace(person.Role) ? "bez roli" : person.Role;
        }

        ViewModel.ScheduleSavePerson(person);
    }

    private async void OnChooseAvatar(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.People.FirstOrDefault(candidate => candidate.Id == _selectedPersonId) is not { } person ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz awatar osoby",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Obrazy")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"]
                }
            ]
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            var imported = await Task.Run(() =>
                new AttachmentStore(ViewModel.DataFolder).ImportImage(person.Id, files[0].Path.LocalPath));
            person.AvatarPath = imported.RelativeFromLibrary;
            ViewModel.ScheduleSavePerson(person);
            Refresh();
            ViewModel.StatusText = "Zmieniono awatar osoby";
        }
        catch (Exception ex)
        {
            ViewModel.StatusText = "Nie udało się dodać awatara: " + ex.Message;
        }
    }

    private void OnRemoveAvatar(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.People.FirstOrDefault(candidate => candidate.Id == _selectedPersonId) is not { } person)
        {
            return;
        }

        person.AvatarPath = string.Empty;
        ViewModel.ScheduleSavePerson(person);
        Refresh();
    }

    private void OnRelatedItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: PersonRelatedItem item } || ViewModel is null)
        {
            return;
        }

        if (item.IsProject)
        {
            var project = ViewModel.Projects.FirstOrDefault(candidate => candidate.Id == item.SourceId);
            if (project is not null)
            {
                ProjectOpenRequested?.Invoke(project);
            }
        }
        else
        {
            var note = ViewModel.Notes.FirstOrDefault(candidate => candidate.Id == item.SourceId);
            if (note is not null)
            {
                NoteOpenRequested?.Invoke(note);
            }
        }
    }

    private string InvolvementSummary(Person person)
    {
        if (ViewModel is null)
        {
            return string.Empty;
        }

        var projects = ViewModel.Projects.Count(project => PersonTagService.Contains(project.People, person.Slug));
        var notes = ViewModel.Notes.Count(note => PersonTagService.Contains(note.People, person.Slug));
        var tasks = ViewModel.Projects.SelectMany(project => project.Checklist)
            .Concat(ViewModel.Notes.SelectMany(note => note.Checklist))
            .Count(task => PersonTagService.Contains(task.People, person.Slug) && !task.IsDone);
        return $"{projects} elementów struktury · {notes} notatek · {tasks} otwartych zadań";
    }

    private string? ResolveAvatarPath(Person person)
    {
        if (ViewModel is null || string.IsNullOrWhiteSpace(person.AvatarPath))
        {
            return null;
        }

        var root = System.IO.Path.GetFullPath(ViewModel.DataFolder)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var relative = person.AvatarPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        return fullPath.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath)
            ? fullPath
            : null;
    }

    private IBrush ResourceBrush(string key, IBrush fallback) =>
        Application.Current?.TryGetResource(key, ActualThemeVariant, out var value) == true && value is IBrush brush
            ? brush
            : fallback;

    private static IBrush AvatarBrush(string seed)
    {
        Color[] colors =
        [
            Color.FromRgb(52, 103, 199),
            Color.FromRgb(18, 135, 124),
            Color.FromRgb(139, 92, 190),
            Color.FromRgb(198, 104, 67),
            Color.FromRgb(51, 127, 156)
        ];
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(seed ?? string.Empty);
        return new SolidColorBrush(colors[(hash & int.MaxValue) % colors.Length]);
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "?";
        }

        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }
}

public sealed record PersonRelatedItem(
    string Title,
    string Meta,
    string SourceId,
    bool IsProject,
    bool IsDone = false);
