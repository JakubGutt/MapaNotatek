using MapaNotatek.Models;

namespace MapaNotatek.Services;

public sealed class MarkdownStore
{
    public string Root { get; private set; }
    public string ProjectsFolder => Path.Combine(Root, "Projects");
    public string NotesFolder => Path.Combine(Root, "Notes");
    public string TrashFolder => Path.Combine(Root, "Trash");

    public MarkdownStore(string root)
    {
        Root = root;
        EnsureFolders();
    }

    public void SetRoot(string root)
    {
        Root = root;
        EnsureFolders();
    }

    public void EnsureFolders()
    {
        Directory.CreateDirectory(ProjectsFolder);
        Directory.CreateDirectory(NotesFolder);
        Directory.CreateDirectory(TrashFolder);
    }

    public List<Project> LoadProjects()
    {
        var projects = new List<Project>();
        foreach (var file in Directory.GetFiles(ProjectsFolder, "*.md"))
        {
            var project = ReadProject(file);
            if (project is not null)
            {
                projects.Add(project);
            }
        }

        return projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public List<Note> LoadNotes()
    {
        var notes = new List<Note>();
        foreach (var file in Directory.GetFiles(NotesFolder, "*.md"))
        {
            var note = ReadNote(file);
            if (note is not null)
            {
                notes.Add(note);
            }
        }

        return notes.OrderByDescending(n => n.Modified).ToList();
    }

    public Project CreateProject(string name)
    {
        var existingSlugs = LoadProjects().Select(p => p.Slug);
        var slug = SlugHelper.Unique(SlugHelper.FromName(name), existingSlugs);
        var now = DateTimeOffset.Now;
        var project = new Project
        {
            Id = SlugHelper.NewId(),
            Name = name.Trim(),
            Slug = slug,
            Description = string.Empty,
            Created = now,
            Modified = now
        };
        SaveProject(project);
        return project;
    }

    public Note CreateNote(string title, IEnumerable<string>? tags = null)
    {
        var now = DateTimeOffset.Now;
        var note = new Note
        {
            Id = SlugHelper.NewId(),
            Title = string.IsNullOrWhiteSpace(title) ? "Nowa notatka" : title.Trim(),
            Body = string.Empty,
            Tags = tags?.ToList() ?? [],
            Created = now,
            Modified = now
        };
        SaveNote(note);
        return note;
    }

    public void SaveProject(Project project)
    {
        project.Modified = DateTimeOffset.Now;
        var fileName = $"{project.Slug}.md";
        var path = Path.Combine(ProjectsFolder, fileName);
        if (!string.IsNullOrWhiteSpace(project.FilePath) &&
            !string.Equals(project.FilePath, path, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(project.FilePath))
        {
            File.Delete(project.FilePath);
        }

        File.WriteAllText(path, FrontMatter.WriteProject(project));
        project.FilePath = path;
    }

    public void SaveNote(Note note)
    {
        note.Modified = DateTimeOffset.Now;
        var slug = SlugHelper.FromName(note.Title);
        var fileName = $"{slug}-{note.Id}.md";
        var path = Path.Combine(NotesFolder, fileName);
        if (!string.IsNullOrWhiteSpace(note.FilePath) &&
            !string.Equals(note.FilePath, path, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(note.FilePath))
        {
            File.Delete(note.FilePath);
        }

        File.WriteAllText(path, FrontMatter.WriteNote(note));
        note.FilePath = path;
    }

    public void MoveNoteToTrash(Note note)
    {
        if (string.IsNullOrWhiteSpace(note.FilePath) || !File.Exists(note.FilePath))
        {
            SaveNote(note);
        }

        Directory.CreateDirectory(TrashFolder);
        var dest = Path.Combine(TrashFolder, Path.GetFileName(note.FilePath));
        dest = UniquePath(dest);
        File.Move(note.FilePath, dest, overwrite: false);
        note.FilePath = dest;
    }

    public void RestoreNoteFromTrash(Note note)
    {
        Directory.CreateDirectory(NotesFolder);
        var dest = Path.Combine(NotesFolder, Path.GetFileName(note.FilePath));
        dest = UniquePath(dest);
        if (File.Exists(note.FilePath))
        {
            File.Move(note.FilePath, dest, overwrite: false);
        }
        else
        {
            File.WriteAllText(dest, FrontMatter.WriteNote(note));
        }

        note.FilePath = dest;
    }

    private Project? ReadProject(string path)
    {
        try
        {
            var parsed = FrontMatter.Parse(File.ReadAllText(path));
            var type = parsed["type"] ?? "project";
            if (!string.Equals(type, "project", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var name = string.IsNullOrWhiteSpace(parsed.Title)
                ? Path.GetFileNameWithoutExtension(path)
                : parsed.Title;
            var slug = parsed["slug"];
            if (string.IsNullOrWhiteSpace(slug))
            {
                var tags = FrontMatter.SplitTags(parsed["tags"]);
                slug = tags.FirstOrDefault() ?? SlugHelper.FromName(name);
            }

            return new Project
            {
                Id = string.IsNullOrWhiteSpace(parsed["id"]) ? SlugHelper.NewId() : parsed["id"]!,
                Name = name,
                Slug = slug,
                Description = parsed.Body,
                Checklist = parsed.Checklist,
                IsArchived = FrontMatter.ReadBool(parsed["archived"]),
                Created = FrontMatter.ReadDate(parsed["created"], File.GetCreationTime(path)),
                Modified = FrontMatter.ReadDate(parsed["modified"], File.GetLastWriteTime(path)),
                FilePath = path
            };
        }
        catch
        {
            return null;
        }
    }

    private Note? ReadNote(string path)
    {
        try
        {
            var parsed = FrontMatter.Parse(File.ReadAllText(path));
            var type = parsed["type"] ?? "note";
            if (!string.Equals(type, "note", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var title = string.IsNullOrWhiteSpace(parsed.Title)
                ? Path.GetFileNameWithoutExtension(path)
                : parsed.Title;

            return new Note
            {
                Id = string.IsNullOrWhiteSpace(parsed["id"]) ? SlugHelper.NewId() : parsed["id"]!,
                Title = title,
                Body = parsed.Body,
                Tags = FrontMatter.SplitTags(parsed["tags"]),
                Checklist = parsed.Checklist,
                Created = FrontMatter.ReadDate(parsed["created"], File.GetCreationTime(path)),
                Modified = FrontMatter.ReadDate(parsed["modified"], File.GetLastWriteTime(path)),
                FilePath = path
            };
        }
        catch
        {
            return null;
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var index = 2;
        string candidate;
        do
        {
            candidate = Path.Combine(dir, $"{name}-{index}{ext}");
            index++;
        }
        while (File.Exists(candidate));

        return candidate;
    }
}
