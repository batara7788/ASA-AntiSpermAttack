using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using Asa.Core;
using Microsoft.Win32;

namespace Asa.App.Services;

public sealed class WindowsRegistryStore : IRegistryStore
{
    public object? Read(RegistryRoot root, string path, string name)
    {
        try
        {
            using RegistryKey baseKey = OpenBase(root);
            using RegistryKey? key = baseKey.OpenSubKey(path);
            return key?.GetValue(name);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    public void Write(RegistryRoot root, string path, string name, object value)
    {
        using RegistryKey baseKey = OpenBase(root);
        using RegistryKey key = baseKey.CreateSubKey(path, true);
        if (value is int number)
        {
            key.SetValue(name, number, RegistryValueKind.DWord);
        }
        else
        {
            key.SetValue(name, value.ToString() ?? string.Empty, RegistryValueKind.String);
        }
    }

    public void Delete(RegistryRoot root, string path, string name)
    {
        using RegistryKey baseKey = OpenBase(root);
        using RegistryKey? key = baseKey.OpenSubKey(path, true);
        key?.DeleteValue(name, false);
    }

    private static RegistryKey OpenBase(RegistryRoot root)
    {
        return root == RegistryRoot.CurrentUser
            ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
            : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    }
}

public sealed class TrackerShield
{
    private const string ListUrl = "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=hosts&showintro=0&mimetype=plaintext";
    private readonly HttpClient httpClient;
    private readonly EventJournal journal;

    public TrackerShield(HttpClient httpClient, EventJournal journal)
    {
        this.httpClient = httpClient;
        this.journal = journal;
    }

    public int GetBlockedDomainCount()
    {
        try
        {
            return HostsBlocker.CountBlockedDomains(File.ReadAllText(WebShield.HostsFilePath), HostsBlocker.TrackerBeginMarker, HostsBlocker.TrackerEndMarker);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public async Task<int> UpdateAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ListUrl);
        request.Headers.UserAgent.ParseAdd("ASA-Antivirus/1.0");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        IReadOnlyList<string> domains = HostsBlocker.ParseDomainList(await response.Content.ReadAsStringAsync(cancellationToken));
        if (domains.Count < 100)
        {
            throw new InvalidOperationException("Список трекеров подозрительно короткий, hosts не изменён");
        }

        string hostsPath = WebShield.HostsFilePath;
        string updated = HostsBlocker.ApplyBlockList(File.ReadAllText(hostsPath), domains, HostsBlocker.TrackerBeginMarker, HostsBlocker.TrackerEndMarker);
        WriteHosts(hostsPath, updated);
        journal.Information("Блокировка трекеров обновлена: рекламных и следящих адресов " + domains.Count);
        return domains.Count;
    }

    public void Disable()
    {
        string hostsPath = WebShield.HostsFilePath;
        WriteHosts(hostsPath, HostsBlocker.RemoveBlockList(File.ReadAllText(hostsPath), HostsBlocker.TrackerBeginMarker, HostsBlocker.TrackerEndMarker));
        journal.Information("Блокировка трекеров выключена");
    }

    private static void WriteHosts(string hostsPath, string content)
    {
        string backup = hostsPath + ".asa-backup";
        if (!File.Exists(backup))
        {
            File.Copy(hostsPath, backup);
        }

        FileAttributes attributes = File.GetAttributes(hostsPath);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(hostsPath, attributes & ~FileAttributes.ReadOnly);
        }

        File.WriteAllText(hostsPath, content, new UTF8Encoding(false));
        EncryptedDnsService.TryRun("ipconfig", "/flushdns");
    }
}

public sealed class DnsBackupEntry
{
    public string AdapterId { get; set; } = string.Empty;

    public string AdapterName { get; set; } = string.Empty;

    public List<string> Ipv4 { get; set; } = new();

    public List<string> Ipv6 { get; set; } = new();
}

public sealed record DnsStatus(bool Supported, DnsProvider? ActiveProvider, int ProtectedAdapters, int TotalAdapters);

public sealed class EncryptedDnsService
{
    private const int FirstWindows11Build = 22000;
    private readonly string backupFile;
    private readonly EventJournal journal;

    public EncryptedDnsService(string backupFile, EventJournal journal)
    {
        this.backupFile = backupFile;
        this.journal = journal;
    }

    public static bool IsSupported => Environment.OSVersion.Version.Build >= FirstWindows11Build;

    public DnsStatus GetStatus()
    {
        List<NetworkInterface> adapters = GetActiveAdapters();
        DnsProvider? provider = null;
        int protectedCount = 0;
        foreach (NetworkInterface adapter in adapters)
        {
            List<string> servers = ReadStaticServers(adapter.Id, false);
            DnsProvider? found = servers.Count > 0 ? EncryptedDnsPlan.FindByAddress(servers[0]) : null;
            if (found != null && servers.All(server => EncryptedDnsPlan.FindByAddress(server) == found))
            {
                provider ??= found;
                protectedCount++;
            }
        }

        return new DnsStatus(IsSupported, protectedCount > 0 ? provider : null, protectedCount, adapters.Count);
    }

    public void Enable(DnsProvider provider)
    {
        if (!IsSupported)
        {
            throw new NotSupportedException("Шифрованный DNS на уровне системы есть только в Windows 11.");
        }

        List<NetworkInterface> adapters = GetActiveAdapters();
        if (adapters.Count == 0)
        {
            throw new InvalidOperationException("Не найдено активных сетевых подключений.");
        }

        Dictionary<string, DnsBackupEntry> backup = LoadBackup();
        foreach (NetworkInterface adapter in adapters)
        {
            if (!backup.ContainsKey(adapter.Id))
            {
                List<string> previousIpv4 = ReadStaticServers(adapter.Id, false);
                bool alreadyOurs = previousIpv4.Count > 0 && previousIpv4.All(server => EncryptedDnsPlan.FindByAddress(server) != null);
                backup[adapter.Id] = new DnsBackupEntry
                {
                    AdapterId = adapter.Id,
                    AdapterName = adapter.Name,
                    Ipv4 = alreadyOurs ? new List<string>() : previousIpv4,
                    Ipv6 = alreadyOurs ? new List<string>() : ReadStaticServers(adapter.Id, true)
                };
            }
        }

        SaveBackup(backup);

        foreach (NetworkInterface adapter in adapters)
        {
            bool hasIpv6 = adapter.Supports(NetworkInterfaceComponent.IPv6);
            foreach (string command in EncryptedDnsPlan.BuildEnableCommands(provider, adapter.Name, hasIpv6))
            {
                bool optional = command.StartsWith("dns ", StringComparison.Ordinal) || command.Contains("ipv6", StringComparison.Ordinal);
                RunNetsh(command, optional);
            }
        }

        TryRun("ipconfig", "/flushdns");
        journal.Information($"Шифрованный DNS включён: {provider.Name}, подключений: {adapters.Count}");
    }

    public void Disable()
    {
        Dictionary<string, DnsBackupEntry> backup = LoadBackup();
        foreach (NetworkInterface adapter in GetActiveAdapters())
        {
            backup.TryGetValue(adapter.Id, out DnsBackupEntry? entry);
            foreach (string command in EncryptedDnsPlan.BuildRestoreCommands(adapter.Name, "ipv4", entry?.Ipv4 ?? new List<string>()))
            {
                RunNetsh(command, false);
            }

            if (adapter.Supports(NetworkInterfaceComponent.IPv6))
            {
                foreach (string command in EncryptedDnsPlan.BuildRestoreCommands(adapter.Name, "ipv6", entry?.Ipv6 ?? new List<string>()))
                {
                    RunNetsh(command, true);
                }
            }

            backup.Remove(adapter.Id);
        }

        SaveBackup(backup);
        TryRun("ipconfig", "/flushdns");
        journal.Information("Шифрованный DNS выключен, прежние настройки DNS возвращены");
    }

    public static void TryRun(string fileName, string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(fileName, arguments) { CreateNoWindow = true, UseShellExecute = false });
            process?.WaitForExit(15000);
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static List<NetworkInterface> GetActiveAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .Where(adapter => adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet)
                .Where(adapter => adapter.GetIPProperties().GatewayAddresses.Count > 0)
                .ToList();
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException or NotImplementedException or System.ComponentModel.Win32Exception)
        {
            return new List<NetworkInterface>();
        }
    }

    private static List<string> ReadStaticServers(string adapterId, bool ipv6)
    {
        string service = ipv6 ? "Tcpip6" : "Tcpip";
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}\Parameters\Interfaces\{adapterId}");
        string value = key?.GetValue("NameServer") as string ?? string.Empty;
        return value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static void RunNetsh(string arguments, bool optional)
    {
        var startInfo = new ProcessStartInfo("netsh.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("netsh не запустился");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        if (process.ExitCode != 0 && !optional)
        {
            throw new InvalidOperationException("netsh " + arguments + ": " + output.Trim());
        }
    }

    private Dictionary<string, DnsBackupEntry> LoadBackup()
    {
        if (!File.Exists(backupFile))
        {
            return new Dictionary<string, DnsBackupEntry>();
        }

        try
        {
            List<DnsBackupEntry> entries = JsonSerializer.Deserialize<List<DnsBackupEntry>>(File.ReadAllText(backupFile)) ?? new List<DnsBackupEntry>();
            return entries.ToDictionary(entry => entry.AdapterId);
        }
        catch (JsonException)
        {
            return new Dictionary<string, DnsBackupEntry>();
        }
    }

    private void SaveBackup(Dictionary<string, DnsBackupEntry> backup)
    {
        File.WriteAllText(backupFile, JsonSerializer.Serialize(backup.Values.ToList()));
    }
}

public sealed class PrivacyService
{
    public PrivacyService(ApplicationFolders folders, HttpClient httpClient, EventJournal journal)
    {
        Manager = new PrivacyManager(new WindowsRegistryStore(), Path.Combine(folders.RootFolder, "privacy-backup.json"));
        Trackers = new TrackerShield(httpClient, journal);
        Dns = new EncryptedDnsService(Path.Combine(folders.RootFolder, "dns-backup.json"), journal);
        Settings = PrivacyCatalog.All().Where(setting => IsGroupPresent(setting.Group)).ToList();
    }

    public PrivacyManager Manager { get; }

    public TrackerShield Trackers { get; }

    public EncryptedDnsService Dns { get; }

    public IReadOnlyList<PrivacySetting> Settings { get; }

    public int CalculateScore()
    {
        int applied = Settings.Count(Manager.IsApplied);
        return PrivacyManager.CalculateScore(applied, Settings.Count, Trackers.GetBlockedDomainCount() > 0, Dns.GetStatus().ActiveProvider != null);
    }

    private static bool IsGroupPresent(string group)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return group switch
        {
            PrivacyCatalog.WindowsGroup => true,
            PrivacyCatalog.ChromeGroup => Directory.Exists(Path.Combine(local, "Google", "Chrome", "User Data")) || File.Exists(Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe")),
            PrivacyCatalog.EdgeGroup => File.Exists(Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe")) || File.Exists(Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")),
            PrivacyCatalog.YandexGroup => Directory.Exists(Path.Combine(local, "Yandex", "YandexBrowser")),
            PrivacyCatalog.FirefoxGroup => Directory.Exists(Path.Combine(roaming, "Mozilla", "Firefox")) || File.Exists(Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe")),
            _ => false
        };
    }
}
