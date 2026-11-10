namespace Asa.Core;

public sealed record JunkLocation(string Name, string FolderPath);

public sealed record JunkReport(string Name, long SizeBytes, int FileCount);

public sealed record CleanResult(long FreedBytes, int DeletedFiles, int SkippedFiles);

public sealed class JunkCleaner
{
    private readonly IReadOnlyList<JunkLocation> locations;

    public JunkCleaner(IReadOnlyList<JunkLocation> locations)
    {
        this.locations = locations;
    }

    public IReadOnlyList<JunkLocation> Locations => locations;

    public IReadOnlyList<JunkReport> Analyze()
    {
        var reports = new List<JunkReport>();
        foreach (IGrouping<string, JunkLocation> group in locations.GroupBy(location => location.Name))
        {
            long size = 0;
            int count = 0;
            foreach (JunkLocation location in group)
            {
                foreach (FileInfo file in EnumerateFilesSafely(location.FolderPath))
                {
                    size += file.Length;
                    count++;
                }
            }

            reports.Add(new JunkReport(group.Key, size, count));
        }

        return reports;
    }

    public CleanResult Clean(IEnumerable<string> namesToClean)
    {
        var selectedNames = new HashSet<string>(namesToClean);
        long freed = 0;
        int deleted = 0;
        int skipped = 0;

        foreach (JunkLocation location in locations.Where(location => selectedNames.Contains(location.Name)))
        {
            foreach (FileInfo file in EnumerateFilesSafely(location.FolderPath))
            {
                try
                {
                    long length = file.Length;
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    freed += length;
                    deleted++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }

            DeleteEmptyFolders(location.FolderPath);
        }

        return new CleanResult(freed, deleted, skipped);
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafely(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return Enumerable.Empty<FileInfo>();
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            return new DirectoryInfo(folderPath).EnumerateFiles("*", options).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Enumerable.Empty<FileInfo>();
        }
    }

    private static void DeleteEmptyFolders(string rootFolder)
    {
        if (!Directory.Exists(rootFolder))
        {
            return;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        List<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(rootFolder, "*", options).OrderByDescending(folder => folder.Length).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string folder in folders)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
