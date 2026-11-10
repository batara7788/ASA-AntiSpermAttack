namespace Asa.App.Services;

public sealed class RealTimeMonitor : IDisposable
{
    private readonly ScanService scanService;
    private readonly List<FileSystemWatcher> watchers = new();

    public RealTimeMonitor(ScanService scanService)
    {
        this.scanService = scanService;
    }

    public bool IsRunning => watchers.Count > 0;

    public IReadOnlyList<string> WatchedFolders => watchers.Select(watcher => watcher.Path).ToList();

    public static List<string> GetDefaultFolders()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new List<string>
        {
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
        };

        return folders.Where(folder => !string.IsNullOrEmpty(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Start(IEnumerable<string> folders)
    {
        Stop();
        foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += OnFileEvent;
            watcher.Changed += OnFileEvent;
            watcher.Renamed += OnRenamed;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    public void Stop()
    {
        foreach (FileSystemWatcher watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        watchers.Clear();
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs eventArgs)
    {
        scanService.Enqueue(eventArgs.FullPath, "Защита в реальном времени");
    }

    private void OnRenamed(object sender, RenamedEventArgs eventArgs)
    {
        scanService.Enqueue(eventArgs.FullPath, "Защита в реальном времени");
    }
}
