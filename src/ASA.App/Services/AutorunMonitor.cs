using Asa.Core;
using Microsoft.Win32;

namespace Asa.App.Services;

public sealed class AutorunMonitor : IDisposable
{
    private const string Source = "Слежка за автозагрузкой";

    private static readonly (RegistryHive Hive, RegistryView View, string KeyPath, string Label)[] RegistryLocations =
    {
        (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\Run", "Реестр пользователя: Run"),
        (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "Реестр пользователя: RunOnce"),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run", "Реестр системы: Run"),
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "Реестр системы: RunOnce"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Run", "Реестр системы (32 бит): Run"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "Реестр системы (32 бит): RunOnce")
    };

    private readonly ScanService scanService;
    private readonly EventJournal journal;
    private readonly Notifier notifier;
    private readonly string snapshotFile;
    private System.Threading.Timer? timer;
    private int checking;

    public AutorunMonitor(ScanService scanService, EventJournal journal, Notifier notifier, string snapshotFile)
    {
        this.scanService = scanService;
        this.journal = journal;
        this.notifier = notifier;
        this.snapshotFile = snapshotFile;
    }

    public event Action? EntriesChanged;

    public bool IsRunning => timer != null;

    public void Start()
    {
        Stop();
        if (!File.Exists(snapshotFile))
        {
            AutorunSnapshot.Save(snapshotFile, ReadAllEntries());
        }

        timer = new System.Threading.Timer(_ => CheckForChanges(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    public void Stop()
    {
        timer?.Dispose();
        timer = null;
    }

    public void Dispose()
    {
        Stop();
    }

    public static List<AutorunEntry> ReadAllEntries()
    {
        var entries = new List<AutorunEntry>();
        foreach ((RegistryHive hive, RegistryView view, string keyPath, string label) in RegistryLocations)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = baseKey.OpenSubKey(keyPath);
                if (key == null)
                {
                    continue;
                }

                foreach (string valueName in key.GetValueNames())
                {
                    string command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    entries.Add(new AutorunEntry(label, valueName, command));
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
            }
        }

        AddStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка автозагрузки пользователя");
        AddStartupFolder(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Папка автозагрузки системы");

        return entries
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public bool RemoveEntry(AutorunEntry entry)
    {
        try
        {
            if (entry.Location.StartsWith("Папка", StringComparison.Ordinal))
            {
                File.Delete(entry.Command);
            }
            else
            {
                (RegistryHive hive, RegistryView view, string keyPath, _) = RegistryLocations.First(location => location.Label == entry.Location);
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = baseKey.OpenSubKey(keyPath, true);
                key?.DeleteValue(entry.Name, false);
            }

            journal.Information("Удалено из автозагрузки: " + entry.Name + " (" + entry.Location + ")");
            AutorunSnapshot.Save(snapshotFile, ReadAllEntries());
            EntriesChanged?.Invoke();
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            journal.Warning("Не удалось удалить из автозагрузки " + entry.Name + ": " + exception.Message);
            return false;
        }
    }

    private static void AddStartupFolder(List<AutorunEntry> entries, string folder, string label)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(folder))
        {
            if (string.Equals(Path.GetFileName(file), "desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new AutorunEntry(label, Path.GetFileName(file), file));
        }
    }

    private void CheckForChanges()
    {
        if (Interlocked.Exchange(ref checking, 1) == 1)
        {
            return;
        }

        try
        {
            IReadOnlyList<AutorunEntry> previous = AutorunSnapshot.Load(snapshotFile);
            List<AutorunEntry> current = ReadAllEntries();
            IReadOnlyList<AutorunEntry> changes = AutorunSnapshot.FindNewOrChanged(previous, current);
            bool removedSomething = previous.Count != current.Count;

            AutorunSnapshot.Save(snapshotFile, current);

            foreach (AutorunEntry entry in changes)
            {
                HandleNewEntry(entry).GetAwaiter().GetResult();
            }

            if (changes.Count > 0 || removedSomething)
            {
                EntriesChanged?.Invoke();
            }
        }
        catch (Exception exception)
        {
            journal.Warning("Ошибка проверки автозагрузки: " + exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref checking, 0);
        }
    }

    private async Task HandleNewEntry(AutorunEntry entry)
    {
        string? executable = AutorunSnapshot.ExtractExecutablePath(entry.Command);
        if (executable != null && File.Exists(executable))
        {
            ScanResult result = await scanService.ScanAndHandleAsync(executable, Source, CancellationToken.None);
            if (result.Verdict == ScanVerdict.Infected)
            {
                RemoveEntry(entry);
                return;
            }
        }

        journal.Warning($"Новая программа в автозагрузке: {entry.Name} → {entry.Command} ({entry.Location})");
        notifier.Show("Новая программа в автозагрузке", $"{entry.Name}\n{entry.Command}\nЕсли вы её не ставили, удалите во вкладке «Автозагрузка».", ToolTipIcon.Warning);
    }
}
