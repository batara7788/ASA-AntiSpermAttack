using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using Asa.Core;

namespace Asa.App.Services;

public sealed class ProcessMonitor : IDisposable
{
    private const string Source = "Проверка процессов";
    private readonly ScanService scanService;
    private readonly EventJournal journal;
    private readonly ConcurrentDictionary<string, DateTime> checkedExecutables = new(StringComparer.OrdinalIgnoreCase);
    private ManagementEventWatcher? watcher;

    public ProcessMonitor(ScanService scanService, EventJournal journal)
    {
        this.scanService = scanService;
        this.journal = journal;
    }

    public bool IsRunning => watcher != null;

    public void Start()
    {
        Stop();
        try
        {
            watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessID FROM Win32_ProcessStartTrace"));
            watcher.EventArrived += OnProcessStarted;
            watcher.Start();
        }
        catch (Exception exception)
        {
            journal.Warning("Слежение за процессами не запустилось (нужны права администратора): " + exception.Message);
            watcher = null;
        }
    }

    public void Stop()
    {
        if (watcher == null)
        {
            return;
        }

        try
        {
            watcher.Stop();
        }
        catch (ManagementException)
        {
        }

        watcher.Dispose();
        watcher = null;
    }

    public void Dispose()
    {
        Stop();
    }

    public static List<string> GetRunningExecutables()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string? path = NativeMethods.GetProcessPath(process.Id);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return paths.ToList();
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs eventArgs)
    {
        object? idValue = eventArgs.NewEvent.Properties["ProcessID"]?.Value;
        if (idValue == null)
        {
            return;
        }

        int processId = Convert.ToInt32(idValue);
        Task.Run(() => CheckProcessAsync(processId));
    }

    private async Task CheckProcessAsync(int processId)
    {
        string? path = NativeMethods.GetProcessPath(processId);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        DateTime lastWrite = File.GetLastWriteTimeUtc(path);
        if (checkedExecutables.TryGetValue(path, out DateTime knownWrite) && knownWrite == lastWrite)
        {
            return;
        }

        if (IsTrustedSystemPath(path))
        {
            checkedExecutables[path] = lastWrite;
            return;
        }

        try
        {
            ScanResult result = await scanService.ScanAndHandleAsync(path, Source, CancellationToken.None);
            if (result.Verdict is ScanVerdict.Clean)
            {
                checkedExecutables[path] = lastWrite;
            }
        }
        catch (Exception exception)
        {
            journal.Warning("Ошибка проверки процесса " + path + ": " + exception.Message);
        }
    }

    private static bool IsTrustedSystemPath(string path)
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return path.StartsWith(system + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !path.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase);
    }
}
