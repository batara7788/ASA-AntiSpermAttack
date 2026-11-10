namespace Asa.Core;

public sealed class PathFilter
{
    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".crdownload", ".part", ".partial", ".download", ".opdownload", ".!ut", ".log", ".etl", ".lock",
        ".sqlite", ".sqlite-wal", ".sqlite-shm", ".sqlite-journal", ".db-wal", ".db-shm", ".db-journal", ".ldb", ".jsonlz4"
    };

    private readonly List<string> excludedPrefixes;

    public PathFilter(IEnumerable<string> excludedPaths)
    {
        excludedPrefixes = excludedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeFolder)
            .ToList();
    }

    public bool ShouldScan(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (IgnoredExtensions.Contains(Path.GetExtension(path)))
        {
            return false;
        }

        string normalized = Path.GetFullPath(path);
        foreach (string prefix in excludedPrefixes)
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized + Path.DirectorySeparatorChar, prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeFolder(string path)
    {
        string full = Path.GetFullPath(path);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }
}
