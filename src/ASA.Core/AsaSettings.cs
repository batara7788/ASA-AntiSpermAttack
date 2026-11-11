using System.Text.Json;

namespace Asa.Core;

public sealed class AsaSettings
{
    public bool RealTimeProtectionEnabled { get; set; } = true;

    public List<string> ProtectedFolders { get; set; } = new();

    public bool UsbScanEnabled { get; set; } = true;

    public bool ProcessScanEnabled { get; set; } = true;

    public bool AutorunMonitorEnabled { get; set; } = true;

    public bool RansomwareProtectionEnabled { get; set; } = true;

    public bool WebShieldEnabled { get; set; } = true;

    public bool GameModeEnabled { get; set; } = true;

    public bool SoundsEnabled { get; set; } = true;

    public bool ScheduledScanEnabled { get; set; } = true;

    public DayOfWeek ScheduledScanDay { get; set; } = DayOfWeek.Sunday;

    public int ScheduledScanHour { get; set; } = 3;

    public DateTime LastScheduledScan { get; set; } = DateTime.MinValue;

    public DateTime LastDatabaseUpdate { get; set; } = DateTime.MinValue;

    public DateTime LastWebShieldUpdate { get; set; } = DateTime.MinValue;

    public bool TrackerBlockEnabled { get; set; }

    public DateTime LastTrackerUpdate { get; set; } = DateTime.MinValue;

    public string DnsProviderId { get; set; } = "quad9";

    public string VirusTotalApiKeyProtected { get; set; } = string.Empty;

    public List<string> ExcludedPaths { get; set; } = new();

    public List<string> TrustedFindings { get; set; } = new();

    public long MaximumFileSizeMegabytes { get; set; } = 100;
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string settingsFile;
    private readonly object fileLock = new();

    public SettingsStore(string settingsFile)
    {
        this.settingsFile = settingsFile;
    }

    public AsaSettings Load()
    {
        lock (fileLock)
        {
            if (!File.Exists(settingsFile))
            {
                return new AsaSettings();
            }

            try
            {
                string json = File.ReadAllText(settingsFile);
                return JsonSerializer.Deserialize<AsaSettings>(json, JsonOptions) ?? new AsaSettings();
            }
            catch (JsonException)
            {
                return new AsaSettings();
            }
        }
    }

    public void Save(AsaSettings settings)
    {
        lock (fileLock)
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            string temporaryFile = settingsFile + ".tmp";
            File.WriteAllText(temporaryFile, json);
            File.Move(temporaryFile, settingsFile, true);
        }
    }
}
