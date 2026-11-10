using Asa.Core;

namespace Asa.App.Services;

public sealed class ProtectionHost : IDisposable
{
    private readonly CancellationTokenSource stopSource = new();
    private readonly SettingsStore settingsStore;
    private System.Threading.Timer? maintenanceTimer;
    private int maintenanceRunning;

    public ProtectionHost()
    {
        Folders = ApplicationFolders.CreateDefault();
        settingsStore = new SettingsStore(Folders.SettingsFile);
        Settings = settingsStore.Load();
        if (Settings.ProtectedFolders.Count == 0)
        {
            Settings.ProtectedFolders = RealTimeMonitor.GetDefaultFolders();
            SaveSettings();
        }

        HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        Journal = new EventJournal(Folders.JournalFile);
        Statistics = new ScanStatistics(Folders.StatisticsFile);
        Quarantine = new Quarantine(Folders.QuarantineFolder);
        GameMode = new GameModeDetector { Enabled = Settings.GameModeEnabled };
        Notifier = new Notifier(GameMode);
        Sounds = new SoundService(GameMode, () => Settings.SoundsEnabled);
        Engine = new ClamAvEngine(Folders, Journal, HttpClient);
        Scanner = new ScanService(Engine, Quarantine, Statistics, Journal, Notifier, () => Settings, Folders.RootFolder);
        Scanner.ThreatDetected += _ => Sounds.Play(SoundEvent.ThreatDetected);
        RealTime = new RealTimeMonitor(Scanner);
        Usb = new UsbMonitor(Scanner, Journal, Notifier);
        Processes = new ProcessMonitor(Scanner, Journal);
        Autoruns = new AutorunMonitor(Scanner, Journal, Notifier, Folders.AutorunSnapshotFile);
        Ransomware = new RansomwareGuard(Journal);
        WebShield = new WebShield(HttpClient, Journal);
    }

    public event Action? ModulesChanged;

    public event Action<string>? ScheduledScanRequested;

    public ApplicationFolders Folders { get; }

    public AsaSettings Settings { get; }

    public HttpClient HttpClient { get; }

    public EventJournal Journal { get; }

    public ScanStatistics Statistics { get; }

    public Quarantine Quarantine { get; }

    public GameModeDetector GameMode { get; }

    public Notifier Notifier { get; }

    public SoundService Sounds { get; }

    public ClamAvEngine Engine { get; }

    public ScanService Scanner { get; }

    public RealTimeMonitor RealTime { get; }

    public UsbMonitor Usb { get; }

    public ProcessMonitor Processes { get; }

    public AutorunMonitor Autoruns { get; }

    public RansomwareGuard Ransomware { get; }

    public WebShield WebShield { get; }

    public bool FullyProtected => Engine.State == EngineState.Ready && Settings.RealTimeProtectionEnabled && RealTime.IsRunning;

    public void SaveSettings()
    {
        settingsStore.Save(Settings);
    }

    public async Task StartAsync()
    {
        Journal.Information("ASA запущен");
        ApplyModuleSettings();

        await Engine.StartAsync(Settings.MaximumFileSizeMegabytes, stopSource.Token);
        if (Engine.State == EngineState.Ready)
        {
            Settings.LastDatabaseUpdate = DateTime.Now;
            SaveSettings();
            Sounds.Play(SoundEvent.ProtectionStarted);
        }

        maintenanceTimer = new System.Threading.Timer(_ => RunMaintenance(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
    }

    public void ApplyModuleSettings()
    {
        GameMode.Enabled = Settings.GameModeEnabled;

        if (Settings.RealTimeProtectionEnabled)
        {
            RealTime.Start(Settings.ProtectedFolders);
        }
        else
        {
            RealTime.Stop();
        }

        Toggle(Settings.UsbScanEnabled, Usb.Start, Usb.Stop);
        Toggle(Settings.ProcessScanEnabled, Processes.Start, Processes.Stop);
        Toggle(Settings.AutorunMonitorEnabled, Autoruns.Start, Autoruns.Stop);
        Toggle(Settings.RansomwareProtectionEnabled, Ransomware.Start, Ransomware.Stop);
        ModulesChanged?.Invoke();
    }

    public static List<string> GetQuickScanLocations()
    {
        var locations = RealTimeMonitor.GetDefaultFolders();
        locations.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        locations.AddRange(ProcessMonitor.GetRunningExecutables());
        foreach (AutorunEntry entry in AutorunMonitor.ReadAllEntries())
        {
            string? executable = AutorunSnapshot.ExtractExecutablePath(entry.Command);
            if (executable != null && File.Exists(executable))
            {
                locations.Add(executable);
            }
        }

        return locations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> GetFullScanLocations()
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)
            .ToList();
    }

    public static JunkCleaner CreateJunkCleaner()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var locations = new List<JunkLocation>
        {
            new("Временные файлы пользователя", Path.GetTempPath()),
            new("Временные файлы Windows", Path.Combine(windows, "Temp")),
            new("Кэш обновлений Windows", Path.Combine(windows, "SoftwareDistribution", "Download")),
            new("Отчёты об ошибках", Path.Combine(local, "CrashDumps")),
            new("Отчёты об ошибках", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive")),
            new("Эскизы проводника", Path.Combine(local, "Microsoft", "Windows", "Explorer"))
        };

        AddChromiumCaches(locations, "Кэш Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data"));
        AddChromiumCaches(locations, "Кэш Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"));
        AddChromiumCaches(locations, "Кэш Яндекс Браузера", Path.Combine(local, "Yandex", "YandexBrowser", "User Data"));
        AddChromiumCaches(locations, "Кэш Opera", Path.Combine(roaming, "Opera Software", "Opera Stable"));

        string firefoxProfiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            foreach (string profile in Directory.EnumerateDirectories(firefoxProfiles))
            {
                locations.Add(new JunkLocation("Кэш Firefox", Path.Combine(profile, "cache2")));
            }
        }

        return new JunkCleaner(locations.Where(location => !location.Name.StartsWith("Эскизы", StringComparison.Ordinal) || Directory.Exists(location.FolderPath)).ToList());
    }

    public void Dispose()
    {
        stopSource.Cancel();
        maintenanceTimer?.Dispose();
        RealTime.Dispose();
        Usb.Dispose();
        Processes.Dispose();
        Autoruns.Dispose();
        Ransomware.Dispose();
        Scanner.Dispose();
        Engine.Dispose();
        Statistics.Save();
        HttpClient.Dispose();
    }

    private static void AddChromiumCaches(List<JunkLocation> locations, string name, string userDataFolder)
    {
        if (!Directory.Exists(userDataFolder))
        {
            return;
        }

        IEnumerable<string> profiles = Directory.EnumerateDirectories(userDataFolder)
            .Where(folder => File.Exists(Path.Combine(folder, "Preferences")))
            .Append(userDataFolder);

        foreach (string profile in profiles)
        {
            locations.Add(new JunkLocation(name, Path.Combine(profile, "Cache")));
            locations.Add(new JunkLocation(name, Path.Combine(profile, "Code Cache")));
            locations.Add(new JunkLocation(name, Path.Combine(profile, "GPUCache")));
        }
    }

    private static void Toggle(bool enabled, Action start, Action stop)
    {
        if (enabled)
        {
            start();
        }
        else
        {
            stop();
        }
    }

    private void RunMaintenance()
    {
        if (Interlocked.Exchange(ref maintenanceRunning, 1) == 1)
        {
            return;
        }

        try
        {
            Notifier.FlushPostponedIfFree();
            DateTime now = DateTime.Now;

            if (now - Settings.LastDatabaseUpdate > TimeSpan.FromHours(12) && Engine.State == EngineState.Ready)
            {
                if (Engine.UpdateDatabaseAsync(stopSource.Token).GetAwaiter().GetResult())
                {
                    Sounds.Play(SoundEvent.DatabaseUpdated);
                }

                Settings.LastDatabaseUpdate = now;
                SaveSettings();
            }

            if (Settings.WebShieldEnabled && now - Settings.LastWebShieldUpdate > TimeSpan.FromHours(24))
            {
                try
                {
                    WebShield.UpdateAsync(stopSource.Token).GetAwaiter().GetResult();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Journal.Warning("Веб-щит не обновился: " + exception.Message);
                }

                Settings.LastWebShieldUpdate = now;
                SaveSettings();
            }

            bool scheduledTime = Settings.ScheduledScanEnabled
                && now.DayOfWeek == Settings.ScheduledScanDay
                && now.Hour >= Settings.ScheduledScanHour
                && Settings.LastScheduledScan.Date != now.Date;

            if (scheduledTime && !GameMode.IsUserBusy() && Engine.State == EngineState.Ready)
            {
                Settings.LastScheduledScan = now;
                SaveSettings();
                ScheduledScanRequested?.Invoke("full");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Journal.Warning("Ошибка фонового обслуживания: " + exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref maintenanceRunning, 0);
        }
    }
}
