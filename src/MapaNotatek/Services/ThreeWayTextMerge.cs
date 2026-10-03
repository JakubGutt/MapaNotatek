namespace MapaNotatek.Services;

public static class ThreeWayTextMerge
{
    private const long MaxMatrixCells = 4_000_000;

    public static TextMergeResult Merge(
        string? baseText,
        string? mineText,
        string? theirText,
        string field,
        string label)
    {
        var baseLines = Lines(baseText);
        var mineLines = Lines(mineText);
        var theirLines = Lines(theirText);

        if (mineLines.SequenceEqual(theirLines, StringComparer.Ordinal))
        {
            return TextMergeResult.Fixed(mineLines);
        }

        if (mineLines.SequenceEqual(baseLines, StringComparer.Ordinal))
        {
            return TextMergeResult.Fixed(theirLines);
        }

        if (theirLines.SequenceEqual(baseLines, StringComparer.Ordinal))
        {
            return TextMergeResult.Fixed(mineLines);
        }

        if ((long)baseLines.Count * Math.Max(mineLines.Count, theirLines.Count) > MaxMatrixCells)
        {
            return WholeDocumentConflict(baseLines, mineLines, theirLines, field, label);
        }

        var mineHunks = Diff(baseLines, mineLines);
        var theirHunks = Diff(baseLines, theirLines);
        var parts = new List<TextMergePart>();
        var baseCursor = 0;
        var mineIndex = 0;
        var theirIndex = 0;
        var conflictIndex = 0;

        while (mineIndex < mineHunks.Count || theirIndex < theirHunks.Count)
        {
            var mine = mineIndex < mineHunks.Count ? mineHunks[mineIndex] : null;
            var theirs = theirIndex < theirHunks.Count ? theirHunks[theirIndex] : null;
            var nextStart = Math.Min(mine?.Start ?? int.MaxValue, theirs?.Start ?? int.MaxValue);
            AddFixed(parts, baseLines.Skip(baseCursor).Take(Math.Max(0, nextStart - baseCursor)));
            baseCursor = Math.Max(baseCursor, nextStart);

            if (mine is not null && (theirs is null || !Overlaps(mine, theirs)) &&
                mine.Start <= (theirs?.Start ?? int.MaxValue))
            {
                AddFixed(parts, mine.Replacement);
                baseCursor = Math.Max(baseCursor, mine.End);
                mineIndex++;
                continue;
            }

            if (theirs is not null && (mine is null || !Overlaps(mine, theirs)))
            {
                AddFixed(parts, theirs.Replacement);
                baseCursor = Math.Max(baseCursor, theirs.End);
                theirIndex++;
                continue;
            }

            var clusterStart = nextStart;
            var clusterEnd = Math.Max(mine?.End ?? clusterStart, theirs?.End ?? clusterStart);
            var clusterMine = new List<DiffHunk>();
            var clusterTheirs = new List<DiffHunk>();
            var expanded = true;
            while (expanded)
            {
                expanded = false;
                while (mineIndex < mineHunks.Count && Touches(mineHunks[mineIndex], clusterStart, clusterEnd))
                {
                    var hunk = mineHunks[mineIndex++];
                    clusterMine.Add(hunk);
                    if (hunk.End > clusterEnd)
                    {
                        clusterEnd = hunk.End;
                        expanded = true;
                    }
                }

                while (theirIndex < theirHunks.Count && Touches(theirHunks[theirIndex], clusterStart, clusterEnd))
                {
                    var hunk = theirHunks[theirIndex++];
                    clusterTheirs.Add(hunk);
                    if (hunk.End > clusterEnd)
                    {
                        clusterEnd = hunk.End;
                        expanded = true;
                    }
                }
            }

            var baseChunk = baseLines.Skip(clusterStart).Take(clusterEnd - clusterStart).ToList();
            var mineChunk = Apply(baseLines, clusterStart, clusterEnd, clusterMine);
            var theirChunk = Apply(baseLines, clusterStart, clusterEnd, clusterTheirs);
            if (mineChunk.SequenceEqual(theirChunk, StringComparer.Ordinal))
            {
                AddFixed(parts, mineChunk);
            }
            else
            {
                var conflict = new MergeConflict
                {
                    Id = $"{field}:{clusterStart}:{conflictIndex++}",
                    Field = field,
                    Label = label,
                    BaseText = Join(baseChunk),
                    MineText = Join(mineChunk),
                    TheirText = Join(theirChunk),
                    CanKeepBoth = true
                };
                parts.Add(new TextMergePart([], conflict));
            }

            baseCursor = Math.Max(baseCursor, clusterEnd);
        }

        AddFixed(parts, baseLines.Skip(baseCursor));
        return new TextMergeResult(parts);
    }

    private static TextMergeResult WholeDocumentConflict(
        IReadOnlyList<string> baseLines,
        IReadOnlyList<string> mineLines,
        IReadOnlyList<string> theirLines,
        string field,
        string label)
    {
        var conflict = new MergeConflict
        {
            Id = field + ":document",
            Field = field,
            Label = label,
            BaseText = Join(baseLines),
            MineText = Join(mineLines),
            TheirText = Join(theirLines),
            CanKeepBoth = true
        };
        return new TextMergeResult([new TextMergePart([], conflict)]);
    }

    private static List<DiffHunk> Diff(IReadOnlyList<string> source, IReadOnlyList<string> target)
    {
        var matrix = new int[source.Count + 1, target.Count + 1];
        for (var left = source.Count - 1; left >= 0; left--)
        {
            for (var right = target.Count - 1; right >= 0; right--)
            {
                matrix[left, right] = string.Equals(source[left], target[right], StringComparison.Ordinal)
                    ? matrix[left + 1, right + 1] + 1
                    : Math.Max(matrix[left + 1, right], matrix[left, right + 1]);
            }
        }

        var hunks = new List<DiffHunk>();
        var sourceIndex = 0;
        var targetIndex = 0;
        DiffHunkBuilder? current = null;
        while (sourceIndex < source.Count || targetIndex < target.Count)
        {
            if (sourceIndex < source.Count && targetIndex < target.Count &&
                string.Equals(source[sourceIndex], target[targetIndex], StringComparison.Ordinal))
            {
                Finish();
                sourceIndex++;
                targetIndex++;
                continue;
            }

            current ??= new DiffHunkBuilder(sourceIndex);
            if (targetIndex < target.Count &&
                (sourceIndex >= source.Count || matrix[sourceIndex, targetIndex + 1] >= matrix[sourceIndex + 1, targetIndex]))
            {
                current.Replacement.Add(target[targetIndex++]);
            }
            else
            {
                current.Deleted++;
                sourceIndex++;
            }
        }

        Finish();
        return hunks;

        void Finish()
        {
            if (current is null)
            {
                return;
            }

            hunks.Add(new DiffHunk(current.Start, current.Deleted, current.Replacement));
            current = null;
        }
    }

    private static List<string> Apply(
        IReadOnlyList<string> baseLines,
        int start,
        int end,
        IReadOnlyList<DiffHunk> hunks)
    {
        var result = new List<string>();
        var cursor = start;
        foreach (var hunk in hunks.OrderBy(item => item.Start))
        {
            result.AddRange(baseLines.Skip(cursor).Take(Math.Max(0, hunk.Start - cursor)));
            result.AddRange(hunk.Replacement);
            cursor = Math.Max(cursor, hunk.End);
        }

        result.AddRange(baseLines.Skip(cursor).Take(Math.Max(0, end - cursor)));
        return result;
    }

    private static bool Overlaps(DiffHunk left, DiffHunk right)
    {
        if (left.Deleted == 0 && right.Deleted == 0)
        {
            return left.Start == right.Start;
        }

        if (left.Deleted == 0)
        {
            return left.Start >= right.Start && left.Start < right.End;
        }

        if (right.Deleted == 0)
        {
            return right.Start >= left.Start && right.Start < left.End;
        }

        return left.Start < right.End && right.Start < left.End;
    }

    private static bool Touches(DiffHunk hunk, int start, int end)
    {
        if (start == end)
        {
            return hunk.Start == start;
        }

        if (hunk.Deleted == 0)
        {
            return hunk.Start >= start && hunk.Start < end;
        }

        return hunk.Start < end && hunk.End > start;
    }

    private static void AddFixed(List<TextMergePart> parts, IEnumerable<string> lines)
    {
        var materialized = lines.ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        if (parts.LastOrDefault() is { Conflict: null } previous)
        {
            previous.Lines.AddRange(materialized);
        }
        else
        {
            parts.Add(new TextMergePart(materialized, null));
        }
    }

    private static List<string> Lines(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();

    private static string Join(IEnumerable<string> lines) => string.Join("\n", lines);

    private sealed record DiffHunk(int Start, int Deleted, List<string> Replacement)
    {
        public int End => Start + Deleted;
    }

    private sealed class DiffHunkBuilder(int start)
    {
        public int Start { get; } = start;
        public int Deleted { get; set; }
        public List<string> Replacement { get; } = [];
    }
}

public sealed class TextMergeResult
{
    internal TextMergeResult(List<TextMergePart> parts)
    {
        Parts = parts;
    }

    internal List<TextMergePart> Parts { get; }
    public IReadOnlyList<MergeConflict> Conflicts => Parts
        .Where(part => part.Conflict is not null)
        .Select(part => part.Conflict!)
        .ToList();

    public string Resolve(IReadOnlyDictionary<string, MergeResolution> resolutions)
    {
        var lines = new List<string>();
        foreach (var part in Parts)
        {
            if (part.Conflict is null)
            {
                lines.AddRange(part.Lines);
                continue;
            }

            if (!resolutions.TryGetValue(part.Conflict.Id, out var resolution) ||
                resolution == MergeResolution.Unresolved)
            {
                throw new InvalidOperationException($"Konflikt „{part.Conflict.Label}” nie został rozwiązany.");
            }

            var selected = resolution switch
            {
                MergeResolution.Mine => part.Conflict.MineText,
                MergeResolution.Theirs => part.Conflict.TheirText,
                MergeResolution.Both when part.Conflict.CanKeepBoth => Combine(part.Conflict.MineText, part.Conflict.TheirText),
                _ => throw new InvalidOperationException("Wybrano niedozwolony sposób rozwiązania konfliktu.")
            };
            lines.AddRange(Lines(selected));
        }

        return Join(lines);
    }

    public static TextMergeResult Fixed(IEnumerable<string> lines) =>
        new([new TextMergePart(lines.ToList(), null)]);

    private static string Combine(string mine, string theirs)
    {
        if (string.Equals(mine, theirs, StringComparison.Ordinal))
        {
            return mine;
        }

        if (string.IsNullOrWhiteSpace(mine))
        {
            return theirs;
        }

        if (string.IsNullOrWhiteSpace(theirs))
        {
            return mine;
        }

        return mine.TrimEnd() + "\n" + theirs.TrimStart();
    }

    private static List<string> Lines(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();

    private static string Join(IEnumerable<string> lines) => string.Join("\n", lines);
}

internal sealed record TextMergePart(List<string> Lines, MergeConflict? Conflict);
