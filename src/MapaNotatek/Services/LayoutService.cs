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

    /// <summary>
    /// Creates a deterministic product-centric star layout. Positions are calculated
    /// for the complete requested scope, including nodes currently hidden by folders.
    /// </summary>
    public static Dictionary<string, GraphPosition> CreateStarLayout(
        IReadOnlyList<Project> projects,
        IReadOnlyList<Note> notes)
    {
        const double projectWidth = 184;
        const double projectHeight = 76;
        const double noteWidth = 166;
        const double noteHeight = 58;
        const double gap = 30;
        var result = new Dictionary<string, GraphPosition>(StringComparer.OrdinalIgnoreCase);
        var occupied = new List<(double Left, double Top, double Right, double Bottom)>();
        var byId = projects.ToDictionary(project => project.Id, StringComparer.OrdinalIgnoreCase);
        var depth = projects.ToDictionary(project => project.Id, project => Depth(projects, project), StringComparer.OrdinalIgnoreCase);

        void Place(string id, double preferredX, double preferredY, double width, double height)
        {
            for (var attempt = 0; attempt < 600; attempt++)
            {
                var angle = attempt * 2.399963229728653;
                var radius = attempt == 0 ? 0 : 26 * Math.Sqrt(attempt);
                var x = Math.Clamp(preferredX + radius * Math.Cos(angle),
                    CanvasEdgeInset + width / 2, CanvasWidth - CanvasEdgeInset - width / 2);
                var y = Math.Clamp(preferredY + radius * Math.Sin(angle),
                    CanvasEdgeInset + height / 2, CanvasHeight - CanvasEdgeInset - height / 2);
                var rect = (x - width / 2 - gap, y - height / 2 - gap,
                    x + width / 2 + gap, y + height / 2 + gap);
                if (occupied.All(other =>
                        rect.Item3 <= other.Left || rect.Item1 >= other.Right ||
                        rect.Item4 <= other.Top || rect.Item2 >= other.Bottom))
                {
                    result[id] = new GraphPosition { Id = id, X = x, Y = y };
                    occupied.Add((rect.Item1, rect.Item2, rect.Item3, rect.Item4));
                    return;
                }
            }

            PlaceAt(result, id, preferredX, preferredY);
        }

        Project? AncestorOfType(Project project, ProjectItemType type)
        {
            var current = project;
            var guard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (guard.Add(current.Id))
            {
                if (current.ItemType == type)
                {
                    return current;
                }

                if (string.IsNullOrWhiteSpace(current.ParentId) || !byId.TryGetValue(current.ParentId, out current!))
                {
                    return null;
                }
            }

            return null;
        }

        var systems = projects.Where(project => project.ItemType == ProjectItemType.System)
            .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(project => project.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Project? OwningSystem(Project product)
        {
            var structural = AncestorOfType(product, ProjectItemType.System);
            if (structural is not null)
            {
                return structural;
            }

            return systems.Where(system => product.SystemIds.Contains(system.Id, StringComparer.OrdinalIgnoreCase))
                .OrderBy(system => system.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(system => system.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        var products = projects.Where(project => project.ItemType == ProjectItemType.Product)
            .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(project => project.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var systemProducts = systems.ToDictionary(
            system => system.Id,
            system => products.Where(product => OwningSystem(product)?.Id == system.Id).ToList(),
            StringComparer.OrdinalIgnoreCase);
        var regionRoots = systems.Cast<Project>()
            .Concat(products.Where(product => OwningSystem(product) is null))
            .Concat(projects.Where(project =>
                project.ItemType is ProjectItemType.Project or ProjectItemType.Folder &&
                AncestorOfType(project, ProjectItemType.Product) is null &&
                AncestorOfType(project, ProjectItemType.System) is null &&
                (string.IsNullOrWhiteSpace(project.ParentId) || !byId.ContainsKey(project.ParentId))))
            .DistinctBy(project => project.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(project => project.ItemType.SortOrder())
            .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (regionRoots.Count == 0)
        {
            regionRoots.AddRange(projects
                .OrderBy(project => project.ItemType.SortOrder())
                .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(1));
        }

        var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(regionRoots.Count)));
        var rows = Math.Max(1, (int)Math.Ceiling(regionRoots.Count / (double)columns));
        var xStep = Math.Min(1750, 3000d / columns);
        var yStep = Math.Min(1750, 3000d / rows);
        var startX = 2000 - ((columns - 1) * xStep / 2);
        var startY = 2000 - ((rows - 1) * yStep / 2);
        var productCenters = new Dictionary<string, (double X, double Y)>(StringComparer.OrdinalIgnoreCase);

        for (var rootIndex = 0; rootIndex < regionRoots.Count; rootIndex++)
        {
            var root = regionRoots[rootIndex];
            var rootX = startX + (rootIndex % columns) * xStep;
            var rootY = startY + (rootIndex / columns) * yStep;
            if (root.ItemType == ProjectItemType.System)
            {
                Place(root.Id, rootX, rootY, projectWidth, projectHeight);
                var ownedProducts = systemProducts[root.Id];
                var radius = Math.Max(540, ownedProducts.Count * 190 / Math.PI);
                for (var index = 0; index < ownedProducts.Count; index++)
                {
                    var angle = -Math.PI / 2 + 2 * Math.PI * index / Math.Max(1, ownedProducts.Count);
                    productCenters[ownedProducts[index].Id] =
                        (rootX + radius * Math.Cos(angle), rootY + radius * Math.Sin(angle));
                }
            }
            else if (root.ItemType == ProjectItemType.Product)
            {
                productCenters[root.Id] = (rootX, rootY);
            }
            else
            {
                Place(root.Id, rootX, rootY, projectWidth, projectHeight);
            }
        }

        foreach (var product in products)
        {
            if (!productCenters.TryGetValue(product.Id, out var center))
            {
                center = (2000, 2000);
                productCenters[product.Id] = center;
            }

            Place(product.Id, center.X, center.Y, projectWidth, projectHeight);
            var members = projects.Where(project => AncestorOfType(project, ProjectItemType.Product)?.Id == product.Id)
                .Where(project => project.Id != product.Id)
                .ToList();
            var subsystems = members.Where(project => project.ItemType == ProjectItemType.Subsystem)
                .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var subsystemAngles = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < subsystems.Count; index++)
            {
                var angle = -Math.PI / 2 + 2 * Math.PI * index / Math.Max(1, subsystems.Count);
                subsystemAngles[subsystems[index].Id] = angle;
                Place(subsystems[index].Id,
                    center.X + 270 * Math.Cos(angle), center.Y + 270 * Math.Sin(angle),
                    projectWidth, projectHeight);
            }

            var components = members.Where(project => project.ItemType == ProjectItemType.Component)
                .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var directComponents = components.Where(component =>
                AncestorOfType(component, ProjectItemType.Subsystem) is null).ToList();
            for (var index = 0; index < directComponents.Count; index++)
            {
                var angle = -Math.PI / 2 + 2 * Math.PI * index / Math.Max(1, directComponents.Count);
                Place(directComponents[index].Id,
                    center.X + 490 * Math.Cos(angle), center.Y + 490 * Math.Sin(angle),
                    projectWidth, projectHeight);
            }

            foreach (var subsystem in subsystems)
            {
                var sectorComponents = components.Where(component =>
                    AncestorOfType(component, ProjectItemType.Subsystem)?.Id == subsystem.Id).ToList();
                var baseAngle = subsystemAngles[subsystem.Id];
                for (var index = 0; index < sectorComponents.Count; index++)
                {
                    var offset = (index - (sectorComponents.Count - 1) / 2d) * 0.18;
                    var angle = baseAngle + offset;
                    Place(sectorComponents[index].Id,
                        center.X + 500 * Math.Cos(angle), center.Y + 500 * Math.Sin(angle),
                        projectWidth, projectHeight);
                }
            }

            var otherMembers = members.Where(member => !result.ContainsKey(member.Id))
                .OrderBy(member => depth[member.Id])
                .ThenBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            for (var index = 0; index < otherMembers.Count; index++)
            {
                var angle = -Math.PI / 2 + 2 * Math.PI * index / Math.Max(1, otherMembers.Count);
                Place(otherMembers[index].Id,
                    center.X + 620 * Math.Cos(angle), center.Y + 620 * Math.Sin(angle),
                    projectWidth, projectHeight);
            }
        }

        var leftovers = projects.Where(project => !result.ContainsKey(project.Id))
            .OrderBy(project => depth[project.Id])
            .ThenBy(project => project.ItemType.SortOrder())
            .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (var index = 0; index < leftovers.Count; index++)
        {
            GraphPosition? parent = null;
            var parentId = leftovers[index].ParentId;
            if (!string.IsNullOrWhiteSpace(parentId) && result.TryGetValue(parentId, out var parentPosition))
            {
                parent = parentPosition;
            }
            var angle = index * 2.399963229728653;
            Place(leftovers[index].Id,
                (parent?.X ?? 2000) + 260 * Math.Cos(angle),
                (parent?.Y ?? 2000) + 260 * Math.Sin(angle),
                projectWidth, projectHeight);
        }

        var noteOwners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in notes.OrderBy(note => note.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(note => note.Id))
        {
            var owner = projects.Where(project => result.ContainsKey(project.Id) && NoteLinksTo(note, project))
                .OrderByDescending(project => depth[project.Id])
                .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();
            var productOwner = owner is null ? null : AncestorOfType(owner, ProjectItemType.Product);
            var anchorOwner = productOwner ?? owner;
            var ownerKey = anchorOwner?.Id ?? string.Empty;
            noteOwners.TryGetValue(ownerKey, out var index);
            noteOwners[ownerKey] = index + 1;
            var anchor = anchorOwner is null ? new GraphPosition { X = 2000, Y = 2000 } : result[anchorOwner.Id];
            var ring = productOwner is not null
                ? 720 + 90 * (index / 10)
                : owner is null ? 760 : 240 + 90 * (index / 8);
            var angle = -Math.PI / 2 + 2 * Math.PI * (index % 8) / 8 + (index / 8) * 0.17;
            Place(note.Id,
                anchor.X + ring * Math.Cos(angle), anchor.Y + ring * Math.Sin(angle),
                noteWidth, noteHeight);
        }

        return result;
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
