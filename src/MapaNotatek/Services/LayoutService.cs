using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class LayoutService
{
    public const double CanvasWidth = 4000;
    public const double CanvasHeight = 4000;
    public const double ProjectDiameter = 72;
    public const double NoteDiameter = 36;

    public static void ApplyMissingPositions(
        IReadOnlyList<Project> projects,
        IReadOnlyList<Note> notes,
        Dictionary<string, GraphPosition> positions)
    {
        var centerX = CanvasWidth / 2;
        var centerY = CanvasHeight / 2;
        var count = Math.Max(projects.Count, 1);
        var ring = 260 + count * 18;

        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            if (positions.ContainsKey(project.Id))
            {
                continue;
            }

            var angle = (2 * Math.PI * i / count) - (Math.PI / 2);
            positions[project.Id] = new GraphPosition
            {
                Id = project.Id,
                X = centerX + (ring * Math.Cos(angle)),
                Y = centerY + (ring * Math.Sin(angle))
            };
        }

        var notesAround = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unassignedIndex = 0;

        foreach (var note in notes)
        {
            if (positions.ContainsKey(note.Id))
            {
                continue;
            }

            var linked = projects.Where(p => NoteLinksTo(note, p)).ToList();
            if (linked.Count == 0)
            {
                positions[note.Id] = new GraphPosition
                {
                    Id = note.Id,
                    X = 180 + ((unassignedIndex % 4) * 90),
                    Y = 180 + ((unassignedIndex / 4) * 90)
                };
                unassignedIndex++;
                continue;
            }

            if (linked.Count >= 2)
            {
                var xs = linked.Select(p => Get(positions, p.Id).X).Average();
                var ys = linked.Select(p => Get(positions, p.Id).Y).Average();
                positions[note.Id] = new GraphPosition { Id = note.Id, X = xs, Y = ys };
                continue;
            }

            var host = linked[0];
            var hostPos = Get(positions, host.Id);
            notesAround.TryGetValue(host.Id, out var aroundIndex);
            notesAround[host.Id] = aroundIndex + 1;
            var aroundAngle = (2 * Math.PI * aroundIndex / 8) + 0.4;
            positions[note.Id] = new GraphPosition
            {
                Id = note.Id,
                X = hostPos.X + (110 * Math.Cos(aroundAngle)),
                Y = hostPos.Y + (110 * Math.Sin(aroundAngle))
            };
        }
    }

    public static bool NoteLinksTo(Note note, Project project) =>
        note.Tags.Any(tag => string.Equals(tag, project.Slug, StringComparison.OrdinalIgnoreCase));

    public static GraphPosition Get(Dictionary<string, GraphPosition> positions, string id)
    {
        if (positions.TryGetValue(id, out var position))
        {
            return position;
        }

        return new GraphPosition { Id = id, X = CanvasWidth / 2, Y = CanvasHeight / 2 };
    }
}
