using System.Diagnostics;
using Asa.Core;

namespace Asa.App.Services;

public sealed record RansomwareSuspect(int ProcessId, string ProcessName, string ExecutablePath, string Reason);

public sealed class RansomwareGuard : IDisposable
{
    private const string CanaryFileName = "!!!_ASA_trap_do_not_touch.docx";
    private const ulong MinimumSuspiciousWriteBytes = 512 * 1024;

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "explorer", "dwm", "MsMpEng", "SearchIndexer", "SearchProtocolHost", "OneDrive", "ASA", "clamd", "freshclam",
        "TiWorker", "TrustedInstaller", "MemCompression", "fontdrvhost", "sihost", "taskhostw", "RuntimeBroker"
    };

    private readonly EventJournal journal;
    private readonly RansomwareDetector detector = new();
    private readonly List<FileSystemWatcher> watchers = new();
    private readonly List<string> canaryPaths = new();
    private readonly object samplesLock = new();
    private Dictionary<int, ulong> previousWriteSamples = new();
    private Dictionary<int, ulong> currentWriteSamples = new();
    private System.Threading.Timer? samplingTimer;
    private bool recreatingCanaries;

    public RansomwareGuard(EventJournal journal)
    {
        this.journal = journal;
    }

    public event Action<RansomwareSuspect?, string>? AttackSuspected;

    public bool IsRunning => watchers.Count > 0;

    public void Start()
    {
        Stop();
        List<string> folders = GetProtectedUserFolders();
        CreateCanaries(folders);

        foreach (string folder in folders)
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += (_, eventArgs) => Record(eventArgs.FullPath);
            watcher.Created += (_, eventArgs) => Record(eventArgs.FullPath);
            watcher.Deleted += (_, eventArgs) => RecordCanaryOnly(eventArgs.FullPath);
            watcher.Renamed += (_, eventArgs) =>
            {
                RecordCanaryOnly(eventArgs.OldFullPath);
                Record(eventArgs.FullPath);
            };
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }

        samplingTimer = new System.Threading.Timer(_ => SampleWriteCounters(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        samplingTimer?.Dispose();
        samplingTimer = null;
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

    public void ResumeSuspect(RansomwareSuspect suspect)
    {
        NativeMethods.ResumeProcess(suspect.ProcessId);
        journal.Information("Процесс возобновлён по решению пользователя: " + suspect.ProcessName);
        RecreateCanaries();
    }

    public void KillSuspect(RansomwareSuspect suspect, Quarantine quarantine)
    {
        try
        {
            using Process process = Process.GetProcessById(suspect.ProcessId);
            process.Kill(true);
            process.WaitForExit(5000);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        try
        {
            if (File.Exists(suspect.ExecutablePath))
            {
                quarantine.Add(suspect.ExecutablePath, "Подозрение на шифровальщик");
            }

            journal.Threat("Шифровальщик остановлен и помещён в карантин: " + suspect.ExecutablePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            journal.Threat("Шифровальщик остановлен, но файл не удалось поместить в карантин: " + exception.Message);
        }

        RecreateCanaries();
    }

    private static List<string> GetProtectedUserFolders()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Path.Combine(profile, "Downloads")
        };

        return folders.Where(folder => !string.IsNullOrEmpty(folder) && Directory.Exists(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void CreateCanaries(IEnumerable<string> folders)
    {
        recreatingCanaries = true;
        try
        {
            canaryPaths.Clear();
            foreach (string folder in folders)
            {
                string path = Path.Combine(folder, CanaryFileName);
                try
                {
                    if (!File.Exists(path))
                    {
                        byte[] content = new byte[4096];
                        Random.Shared.NextBytes(content);
                        File.WriteAllBytes(path, content);
                        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
                    }

                    canaryPaths.Add(path);
                    detector.RegisterCanaryFile(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    journal.Warning("Не удалось создать файл-ловушку в " + folder + ": " + exception.Message);
                }
            }
        }
        finally
        {
            Task.Delay(1500).ContinueWith(_ => recreatingCanaries = false);
        }
    }

    private void RecreateCanaries()
    {
        foreach (string path in canaryPaths.ToList())
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        recreatingCanaries = true;
        CreateCanaries(GetProtectedUserFolders());
    }

    private void RecordCanaryOnly(string path)
    {
        if (detector.IsCanaryFile(path))
        {
            Record(path);
        }
    }

    private void Record(string path)
    {
        if (recreatingCanaries)
        {
            return;
        }

        RansomwareAlert? alert = detector.RecordModification(path, DateTime.Now);
        if (alert == null)
        {
            return;
        }

        RansomwareSuspect? suspect = FindAndSuspendSuspect(alert.Reason);
        if (suspect == null)
        {
            journal.Threat("Похоже на атаку шифровальщика: " + alert.Reason + ". Виновный процесс определить не удалось.");
        }
        else
        {
            journal.Threat($"Похоже на атаку шифровальщика: {alert.Reason}. Процесс {suspect.ProcessName} ({suspect.ExecutablePath}) приостановлен.");
        }

        AttackSuspected?.Invoke(suspect, alert.Reason);
    }

    private void SampleWriteCounters()
    {
        var samples = new Dictionary<int, ulong>();
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                ulong? written = NativeMethods.GetWrittenBytes(process.Id);
                if (written.HasValue)
                {
                    samples[process.Id] = written.Value;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        lock (samplesLock)
        {
            previousWriteSamples = currentWriteSamples;
            currentWriteSamples = samples;
        }
    }

    private RansomwareSuspect? FindAndSuspendSuspect(string reason)
    {
        Dictionary<int, ulong> baseline;
        lock (samplesLock)
        {
            baseline = previousWriteSamples;
        }

        int ownProcessId = Environment.ProcessId;
        RansomwareSuspect? best = null;
        ulong bestDelta = MinimumSuspiciousWriteBytes;

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.Id <= 4 || process.Id == ownProcessId || ProtectedProcessNames.Contains(process.ProcessName))
                {
                    continue;
                }

                ulong? written = NativeMethods.GetWrittenBytes(process.Id);
                if (!written.HasValue)
                {
                    continue;
                }

                ulong before = baseline.TryGetValue(process.Id, out ulong value) ? value : 0;
                ulong delta = written.Value > before ? written.Value - before : 0;
                if (delta > bestDelta)
                {
                    string path = NativeMethods.GetProcessPath(process.Id) ?? string.Empty;
                    best = new RansomwareSuspect(process.Id, process.ProcessName, path, reason);
                    bestDelta = delta;
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        if (best != null)
        {
            NativeMethods.SuspendProcess(best.ProcessId);
        }

        return best;
    }
}
