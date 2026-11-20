namespace DualPlayer.Core.Playback;

/// <summary>Direction of a next/previous file move.</summary>
public enum NeighbourDirection
{
    Next,
    Previous,
}

/// <summary>Lists the .mkv/.mp4 files of a file's folder that a next/previous press may move to, in natural name order.</summary>
public static class FolderNeighbours
{
    public static IEnumerable<string> Candidates(string currentFile, NeighbourDirection direction)
    {
        var full = Path.GetFullPath(currentFile);
        var folder = Path.GetDirectoryName(full)!;
        var currentName = Path.GetFileName(full);

        var names = new List<string>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase) || ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(Path.GetFileName(path));
            }
        }

        names.Sort(NaturalNameComparer.Instance);
        if (direction == NeighbourDirection.Previous)
        {
            names.Reverse();
        }

        return Following(folder, names, currentName, direction);
    }

    private static IEnumerable<string> Following(string folder, List<string> names, string currentName, NeighbourDirection direction)
    {
        var sign = direction == NeighbourDirection.Next ? 1 : -1;
        foreach (var name in names)
        {
            if (NaturalNameComparer.Instance.Compare(name, currentName) * sign > 0)
            {
                yield return Path.Combine(folder, name);
            }
        }
    }

    internal sealed class NaturalNameComparer : IComparer<string>
    {
        public static readonly NaturalNameComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var natural = Natural(x!, y!);
            return natural != 0 ? natural : string.CompareOrdinal(x, y);
        }

        private static int Natural(string x, string y)
        {
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    var si = i;
                    var sj = j;
                    while (i < x.Length && char.IsAsciiDigit(x[i]))
                    {
                        i++;
                    }

                    while (j < y.Length && char.IsAsciiDigit(y[j]))
                    {
                        j++;
                    }

                    var a = x.AsSpan(si, i - si).TrimStart('0');
                    var b = y.AsSpan(sj, j - sj).TrimStart('0');
                    var c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.CompareTo(b, StringComparison.Ordinal);
                    if (c != 0)
                    {
                        return c;
                    }
                }
                else
                {
                    var c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (c != 0)
                    {
                        return c;
                    }

                    i++;
                    j++;
                }
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
