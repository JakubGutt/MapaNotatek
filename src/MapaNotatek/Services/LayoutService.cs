using MapaNotatek.Models;

namespace MapaNotatek.Services;

public static class LayoutService
{
    public const double CanvasWidth = 4000;
    public const double CanvasHeight = 4000;
    public const double ProjectDiameter = 72;
    public const double NoteDiameter = 36;
    // The graph uses labelled cards rather than small dots, so new items need
    // enough breathing room to keep titles and metadata readable.
    private const double MinSeparation = 190;
    private const double CanvasEdgeInset = 110;

    public static void PlaceAt(
        Dictionary<string, GraphPosition> positions,
        string id,
        double x,
        double y)
    {
        positions[id] = new GraphPosition
        {
            Id = id,
            X = Math.Clamp(x, CanvasEdgeInset, CanvasWidth - CanvasEdgeInset),
            Y = Math.Clamp(y, CanvasEdgeInset, CanvasHeight - CanvasEdgeInset)
        };
    }

    /// <summary>
    /// Places a node around an anchor, fanning siblings so they don't stack.
    /// </summary>
    public static void PlaceNear(
        Dictionary<string, GraphPosition> positions,
        string id,
        double anchorX,
        double anchorY,
        int siblingIndex = 0)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var slot = siblingIndex + attempt;
            var ring = slot / 8;
            var onRing = slot % 8;
            var angle = (2 * Math.PI * onRing / 8) + 0.35 + (ring * 0.15);
            var radius = 215 + (ring * 105);
            var x = anchorX + (radius * Math.Cos(angle));
            var y = anchorY + (radius * Math.Sin(angle));
            if (!IsTooClose(positions, id, x, y))
            {
                PlaceAt(positions, id, x, y);
                return;
            }
        }

        PlaceAt(positions, id, anchorX + 160, anchorY + 40);
    }

    /// <summary>
    /// Places at a preferred point, nudging away if something already sits there.
    /// </summary>
    public static void PlaceAtAvoidingOverlap(
        Dictionary<string, GraphPosition> positions,
        string id,
        double preferredX,
        double preferredY)
    {
        if (!IsTooClose(positions, id, preferredX, preferredY))
        {
            PlaceAt(positions, id, preferredX, preferredY);
            return;
        }

        PlaceNear(positions, id, preferredX, preferredY, siblingIndex: 0);
    }

    public static int CountSiblings(
        IEnumerable<Project> projects,
        string? parentId,
        string? excludeId = null)
    {
        var key = parentId ?? string.Empty;
        return projects.Count(p =>
            string.Equals(p.ParentId ?? string.Empty, key, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(p.Id, excludeId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsTooClose(
        Dictionary<string, GraphPosition> positions,
        string excludeId,
        double x,
        double y)
    {
        foreach (var pair in positions)
        {
            if (string.Equals(pair.Key, excludeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dx = pair.Value.X - x;
            var dy = pair.Value.Y - y;
            if ((dx * dx) + (dy * dy) < MinSeparation * MinSeparation)
            {
                return true;
            }
        }

        return false;
    }

    public static void ApplyMissingPositions(
        IReadOnlyList<Project> projects,
        IReadOnlyList<Note> notes,
        Dictionary<string, GraphPosition> positions)
    {
        var centerX = CanvasWidth / 2;
        var centerY = CanvasHeight / 2;
        var roots = projects.Where(p => string.IsNullOrWhiteSpace(p.ParentId)).ToList();
        var count = Math.Max(roots.Count, 1);
        var ring = 260 + count * 18;
        var rootIndex = 0;

        // Parents first so children can attach to them.
        var ordered = projects
            .OrderBy(p => Depth(projects, p))
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var project in ordered)
        {
            if (positions.ContainsKey(project.Id))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(project.ParentId))
            {
                var parentPos = Get(positions, project.ParentId);
                var siblingIndex = CountSiblings(projects, project.ParentId, project.Id);
                PlaceNear(positions, project.Id, parentPos.X, parentPos.Y, siblingIndex);
                continue;
            }

            var angleRoot = (2 * Math.PI * rootIndex / count) - (Math.PI / 2);
            PlaceAtAvoidingOverlap(
                positions,
                project.Id,
                centerX + (ring * Math.Cos(angleRoot)),
                centerY + (ring * Math.Sin(angleRoot)));
            rootIndex++;
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
                PlaceAtAvoidingOverlap(
                    positions,
                    note.Id,
                    centerX - 200 + ((unassignedIndex % 4) * 100),
                    centerY - 200 + ((unassignedIndex / 4) * 100));
                unassignedIndex++;
                continue;
            }

            if (linked.Count >= 2)
            {
                var xs = linked.Select(p => Get(positions, p.Id).X).Average();
                var ys = linked.Select(p => Get(positions, p.Id).Y).Average();
                PlaceAtAvoidingOverlap(positions, note.Id, xs, ys);
                continue;
            }

            var host = linked[0];
            var hostPos = Get(positions, host.Id);
            notesAround.TryGetValue(host.Id, out var aroundIndex);
            notesAround[host.Id] = aroundIndex + 1;
            PlaceNear(positions, note.Id, hostPos.X, hostPos.Y, aroundIndex);
        }
    }

    private static int Depth(IReadOnlyList<Project> projects, Project project)
    {
        var depth = 0;
        var current = project;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current.ParentId) && guard++ < 64)
        {
            depth++;
            current = projects.FirstOrDefault(p => p.Id == current.ParentId) ?? current;
            if (ReferenceEquals(current, project) || string.IsNullOrWhiteSpace(current.ParentId))
            {
                break;
            }

            if (projects.All(p => p.Id != current.ParentId) && depth > 0)
            {
                break;
            }

            var parent = projects.FirstOrDefault(p => p.Id == current.ParentId);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }

        return depth;
    }

    public static bool NoteLinksTo(Note note, Project project) =>
        note.Tags.Any(tag => string.Equals(tag, project.Slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns one project and every nested folder/project below it.</summary>
    public static IReadOnlyList<Project> ProjectSubtree(
        IReadOnlyList<Project> projects,
        string rootProjectId)
    {
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootProjectId };
        var queue = new Queue<string>();
        queue.Enqueue(rootProjectId);
        while (queue.TryDequeue(out var parentId))
        {
            foreach (var child in projects.Where(project =>
                         !included.Contains(project.Id) &&
                         string.Equals(project.ParentId, parentId, StringComparison.OrdinalIgnoreCase)))
            {
                included.Add(child.Id);
                queue.Enqueue(child.Id);
            }
        }

        return projects.Where(project => included.Contains(project.Id)).ToList();
    }

    public static GraphPosition Get(Dictionary<string, GraphPosition> positions, string id)
    {
        if (positions.TryGetValue(id, out var position))
        {
            return position;
        }

        return new GraphPosition { Id = id, X = CanvasWidth / 2, Y = CanvasHeight / 2 };
    }
}
