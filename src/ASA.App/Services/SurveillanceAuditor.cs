using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Asa.Core;
using Microsoft.Win32;

namespace Asa.App.Services;

public sealed class SurveillanceItem
{
    public required string Category { get; init; }

    public required string Title { get; init; }

    public required string Details { get; init; }

    public SurveillanceLevel Level { get; init; }

    public bool Disabled { get; init; }

    public string? DisableLabel { get; init; }

    public Func<string>? Disable { get; init; }

    public string? EnableLabel { get; init; }

    public Func<string>? Enable { get; init; }
}

public sealed class SurveillanceAuditor
{
    private const string ProgramRulePrefix = "ASA block: ";
    private const string PortRulePrefix = "ASA block port TCP ";
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";

    private static readonly HashSet<string> SystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "svchost", "lsass", "services", "wininit", "spoolsv", "MsMpEng", "ASA", "clamd", "freshclam", "Idle"
    };

    private readonly EventJournal journal;
    private readonly string servicesBackupFile;

    public SurveillanceAuditor(ApplicationFolders folders, EventJournal journal)
    {
        this.journal = journal;
        servicesBackupFile = Path.Combine(folders.RootFolder, "services-backup.json");
    }

    public List<SurveillanceItem> Run(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var items = new List<SurveillanceItem>();
        var steps = new (string Name, Action<List<SurveillanceItem>> Check)[]
        {
            ("соединения программ", list => list.AddRange(BuildConnectionItems(NetworkTables.ReadTcpEntries()))),
            ("открытые порты", CheckPorts),
            ("службы телеметрии", CheckServices),
            ("задачи телеметрии", CheckTasks),
            ("камера, микрофон, местоположение", CheckCapabilities),
            ("фоновые программы", CheckBackgroundPrograms),
            ("следы на диске", CheckTraces)
        };

        foreach ((string name, Action<List<SurveillanceItem>> check) in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report("Проверяю: " + name);
            try
            {
                check(items);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                journal.Warning($"Аудит слежки, шаг «{name}»: {exception.Message}");
                items.Add(new SurveillanceItem { Category = "Аудит", Title = "Не удалось проверить: " + name, Details = exception.Message, Level = SurveillanceLevel.Unknown });
            }
        }

        return items;
    }

    public async Task<List<SurveillanceItem>> WatchConnectionsAsync(TimeSpan duration, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var seen = new Dictionary<string, TcpEntry>();
        DateTime end = DateTime.Now + duration;
        while (DateTime.Now < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (TcpEntry entry in NetworkTables.ReadTcpEntries().Where(entry => !entry.Listening))
            {
                seen[entry.ProcessId + "|" + entry.RemoteAddress + "|" + entry.RemotePort] = entry;
            }

            int left = (int)Math.Max(0, (end - DateTime.Now).TotalSeconds);
            progress.Report($"Наблюдаю за соединениями: осталось {left} с, замечено адресов: {seen.Count}");
            await Task.Delay(1500, cancellationToken);
        }

        progress.Report("Определяю, чьи это адреса");
        return await Task.Run(() => BuildConnectionItems(seen.Values.ToList()), cancellationToken);
    }

    private List<SurveillanceItem> BuildConnectionItems(List<TcpEntry> entries)
    {
        var items = new List<SurveillanceItem>();
        List<TcpEntry> outgoing = entries.Where(entry => !entry.Listening && HostClassifier.IsPublicAddress(entry.RemoteAddress)).ToList();
        Dictionary<string, string> names = ResolveHostNames(outgoing.Select(entry => entry.RemoteAddress).Distinct().ToList());
        HashSet<string> trackers = LoadTrackerDomains();
        HashSet<string> blockedPrograms = ReadFirewallRuleNames().Where(name => name.StartsWith(ProgramRulePrefix, StringComparison.Ordinal)).Select(name => name.Substring(ProgramRulePrefix.Length)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<int, TcpEntry> group in outgoing.GroupBy(entry => entry.ProcessId))
        {
            (string processName, string? path) = DescribeProcess(group.Key);
            List<HostOwner> owners = group
                .Select(entry => entry.RemoteAddress)
                .Distinct()
                .Select(address => names.TryGetValue(address.ToString(), out string? host) ? HostClassifier.Classify(host, trackers) : new HostOwner(address.ToString(), HostKind.Unknown))
                .ToList();

            int advertising = owners.Count(owner => owner.Kind == HostKind.Advertising);
            int telemetry = owners.Count(owner => owner.Kind == HostKind.Telemetry);
            int unknown = owners.Count(owner => owner.Kind == HostKind.Unknown);
            bool systemProcess = SystemProcessNames.Contains(processName) || path == null;

            SurveillanceLevel level = advertising > 0 ? SurveillanceLevel.Danger
                : telemetry > 0 ? SurveillanceLevel.Recommended
                : unknown > 0 ? SurveillanceLevel.Unknown
                : SurveillanceLevel.Harmless;

            string summary = string.Join(", ", owners
                .OrderByDescending(owner => owner.Kind)
                .GroupBy(owner => owner.Owner)
                .Select(ownerGroup => ownerGroup.Count() > 1 ? $"{ownerGroup.Key} ×{ownerGroup.Count()}" : ownerGroup.Key)
                .Take(12));

            string verdict = advertising > 0 ? $"Связывается с рекламными сетями и трекерами ({advertising}). "
                : telemetry > 0 ? $"Отправляет телеметрию ({telemetry}). "
                : unknown > 0 ? "Часть адресов опознать не удалось. "
                : "Только известные обычные адреса. ";

            string? capturedPath = path;
            bool canBlock = !systemProcess && capturedPath != null;
            items.Add(new SurveillanceItem
            {
                Category = "Соединения",
                Title = processName + (path != null ? "  (" + path + ")" : string.Empty),
                Details = verdict + $"Адресов: {owners.Count}. Куда: {summary}." + (systemProcess ? " Системный процесс: блокировать нельзя, пропадёт интернет." : string.Empty),
                Level = systemProcess && level != SurveillanceLevel.Harmless ? SurveillanceLevel.Unknown : level,
                DisableLabel = canBlock ? "Запретить программе выход в интернет" : null,
                Disable = canBlock ? () => BlockProgram(capturedPath!) : null
            });
            if (capturedPath != null)
            {
                blockedPrograms.Remove(capturedPath);
            }
        }

        foreach (string blockedPath in blockedPrograms)
        {
            string capturedPath = blockedPath;
            items.Add(new SurveillanceItem
            {
                Category = "Соединения",
                Title = Path.GetFileName(blockedPath) + "  (" + blockedPath + ")",
                Details = "ASA запретил этой программе выход в интернет.",
                Level = SurveillanceLevel.Harmless,
                Disabled = true,
                EnableLabel = "Вернуть программе интернет",
                Enable = () => RemoveFirewallRule(ProgramRulePrefix + capturedPath)
            });
        }

        return items;
    }

    private void CheckPorts(List<SurveillanceItem> items)
    {
        HashSet<string> ruleNames = ReadFirewallRuleNames();
        var shownPorts = new HashSet<int>();
        foreach (IGrouping<int, TcpEntry> group in NetworkTables.ReadTcpEntries().Where(entry => entry.Listening).GroupBy(entry => entry.LocalPort).OrderBy(group => group.Key))
        {
            bool openToNetwork = group.Any(entry => !IPAddress.IsLoopback(entry.LocalAddress));
            if (!openToNetwork)
            {
                continue;
            }

            int port = group.Key;
            shownPorts.Add(port);
            (string processName, string? path) = DescribeProcess(group.First().ProcessId);
            bool systemProcess = SystemProcessNames.Contains(processName) || path == null;
            PortInfo info = PortCatalog.Describe(port, systemProcess);
            bool blocked = ruleNames.Contains(PortRulePrefix + port);
            bool canBlock = info.Level != SurveillanceLevel.DoNotTouch;

            items.Add(new SurveillanceItem
            {
                Category = "Открытые порты",
                Title = $"Порт {port}: {info.Name} — {processName}",
                Details = info.Explanation + (blocked ? " ASA закрыл этот порт в брандмауэре." : " Принимает подключения из сети."),
                Level = blocked ? SurveillanceLevel.Harmless : info.Level,
                Disabled = blocked,
                DisableLabel = canBlock && !blocked ? "Закрыть порт в брандмауэре" : null,
                Disable = canBlock && !blocked ? () => BlockPort(port) : null,
                EnableLabel = blocked ? "Открыть порт обратно" : null,
                Enable = blocked ? () => RemoveFirewallRule(PortRulePrefix + port) : null
            });
        }
    }

    private void CheckServices(List<SurveillanceItem> items)
    {
        Dictionary<string, int> backup = LoadServicesBackup();
        using RegistryKey? services = Registry.LocalMachine.OpenSubKey(ServicesPath);
        if (services == null)
        {
            return;
        }

        foreach (KnownService known in TelemetryCatalog.Services)
        {
            using RegistryKey? service = services.OpenSubKey(known.Name);
            if (service?.GetValue("Start") is not int start)
            {
                continue;
            }

            bool disabled = start == 4;
            bool canChange = known.Level != SurveillanceLevel.DoNotTouch;
            string state = disabled ? "Отключена." : start == 2 ? "Запускается вместе с Windows." : "Запускается по требованию.";
            string capturedName = known.Name;
            items.Add(new SurveillanceItem
            {
                Category = "Службы",
                Title = $"{known.Title} ({known.Name})",
                Details = known.Explanation + " " + state,
                Level = disabled ? SurveillanceLevel.Harmless : known.Level,
                Disabled = disabled,
                DisableLabel = canChange && !disabled ? "Отключить службу" : null,
                Disable = canChange && !disabled ? () => DisableService(capturedName) : null,
                EnableLabel = disabled && canChange ? "Включить службу обратно" : null,
                Enable = disabled && canChange ? () => EnableService(capturedName, backup.TryGetValue(capturedName, out int previous) ? previous : 3) : null
            });
        }
    }

    private void CheckTasks(List<SurveillanceItem> items)
    {
        string tasksFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
        var tasks = TelemetryCatalog.Tasks.ToList();
        if (Directory.Exists(tasksFolder))
        {
            foreach (string file in Directory.EnumerateFiles(tasksFolder, "NvTmRep*"))
            {
                tasks.Add(new KnownTask("\\" + Path.GetFileName(file), "Телеметрия NVIDIA", "Отправляет в NVIDIA отчёты об использовании.", SurveillanceLevel.Recommended));
            }
        }

        foreach (KnownTask known in tasks)
        {
            string file = tasksFolder + known.Path;
            if (!File.Exists(file))
            {
                continue;
            }

            bool? enabled;
            try
            {
                enabled = TelemetryCatalog.ReadTaskEnabled(File.ReadAllText(file));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                enabled = null;
            }

            bool disabled = enabled == false;
            string capturedPath = known.Path;
            items.Add(new SurveillanceItem
            {
                Category = "Задачи",
                Title = $"{known.Title} ({known.Path})",
                Details = known.Explanation + (disabled ? " Отключена." : " Запускается по расписанию."),
                Level = disabled ? SurveillanceLevel.Harmless : known.Level,
                Disabled = disabled,
                DisableLabel = disabled ? null : "Отключить задачу",
                Disable = disabled ? null : () => RunTool("schtasks.exe", $"/Change /TN \"{capturedPath}\" /Disable", "Задача отключена."),
                EnableLabel = disabled ? "Включить задачу обратно" : null,
                Enable = disabled ? () => RunTool("schtasks.exe", $"/Change /TN \"{capturedPath}\" /Enable", "Задача включена.") : null
            });
        }
    }

    private static void CheckCapabilities(List<SurveillanceItem> items)
    {
        var capabilities = new (string Key, string Name, string What)[]
        {
            ("webcam", "Камера", "может снимать вас"),
            ("microphone", "Микрофон", "может слушать вас"),
            ("location", "Местоположение", "знает, где находится компьютер")
        };

        DateTime now = DateTime.Now;
        foreach ((string key, string name, string what) in capabilities)
        {
            using RegistryKey? capabilityKey = Registry.CurrentUser.OpenSubKey(ConsentStore + "\\" + key);
            if (capabilityKey == null)
            {
                continue;
            }

            foreach (string packageName in capabilityKey.GetSubKeyNames().Where(subKey => subKey != "NonPackaged"))
            {
                using RegistryKey? package = capabilityKey.OpenSubKey(packageName);
                string value = package?.GetValue("Value") as string ?? string.Empty;
                DateTime? start = CapabilityAccess.FromFileTime(package?.GetValue("LastUsedTimeStart"));
                DateTime? stop = CapabilityAccess.FromFileTime(package?.GetValue("LastUsedTimeStop"));
                bool denied = string.Equals(value, "Deny", StringComparison.OrdinalIgnoreCase);
                if (start == null && !denied)
                {
                    continue;
                }

                SurveillanceLevel level = CapabilityAccess.Assess(start, stop, now, out string usage);
                string path = ConsentStore + "\\" + key + "\\" + packageName;
                items.Add(new SurveillanceItem
                {
                    Category = name,
                    Title = $"{name}: приложение {CapabilityAccess.DescribePackage(packageName)}",
                    Details = denied ? "Доступ запрещён." : $"Приложение {what}; {usage}.",
                    Level = denied ? SurveillanceLevel.Harmless : level,
                    Disabled = denied,
                    DisableLabel = denied ? null : "Запретить доступ",
                    Disable = denied ? null : () => SetConsent(path, "Deny", "Доступ запрещён."),
                    EnableLabel = denied ? "Разрешить доступ обратно" : null,
                    Enable = denied ? () => SetConsent(path, "Allow", "Доступ разрешён.") : null
                });
            }

            using RegistryKey? nonPackaged = capabilityKey.OpenSubKey("NonPackaged");
            if (nonPackaged == null)
            {
                continue;
            }

            bool allDenied = string.Equals(nonPackaged.GetValue("Value") as string, "Deny", StringComparison.OrdinalIgnoreCase);
            var users = new List<string>();
            SurveillanceLevel worst = SurveillanceLevel.Harmless;
            foreach (string programKey in nonPackaged.GetSubKeyNames())
            {
                using RegistryKey? program = nonPackaged.OpenSubKey(programKey);
                DateTime? start = CapabilityAccess.FromFileTime(program?.GetValue("LastUsedTimeStart"));
                DateTime? stop = CapabilityAccess.FromFileTime(program?.GetValue("LastUsedTimeStop"));
                if (start == null)
                {
                    continue;
                }

                SurveillanceLevel level = CapabilityAccess.Assess(start, stop, now, out string usage);
                if (level > worst)
                {
                    worst = level;
                }

                users.Add(Path.GetFileName(CapabilityAccess.DecodeProgramPath(programKey)) + " (" + usage + ")");
            }

            if (users.Count == 0 && !allDenied)
            {
                continue;
            }

            string nonPackagedPath = ConsentStore + "\\" + key + "\\NonPackaged";
            items.Add(new SurveillanceItem
            {
                Category = name,
                Title = $"{name}: обычные программы ({users.Count})",
                Details = allDenied
                    ? "Обычным программам доступ запрещён."
                    : "Пользовались: " + string.Join("; ", users.Take(10)) + ". Windows умеет запретить доступ только всем обычным программам сразу: тогда перестанут работать звонки в Discord, Zoom и подобных.",
                Level = allDenied ? SurveillanceLevel.Harmless : worst == SurveillanceLevel.Recommended ? SurveillanceLevel.Unknown : worst,
                Disabled = allDenied,
                DisableLabel = allDenied ? null : "Запретить всем обычным программам",
                Disable = allDenied ? null : () => SetConsent(nonPackagedPath, "Deny", "Доступ обычным программам запрещён."),
                EnableLabel = allDenied ? "Разрешить обратно" : null,
                Enable = allDenied ? () => SetConsent(nonPackagedPath, "Allow", "Доступ обычным программам разрешён.") : null
            });
        }
    }

    private static void CheckBackgroundPrograms(List<SurveillanceItem> items)
    {
        foreach (AutorunEntry entry in AutorunMonitor.ReadAllEntries())
        {
            (RegistryHive hive, string approvalKey, string valueName)? approval = FindApprovalLocation(entry);
            bool disabled = approval != null && StartupApproval.IsDisabled(ReadApproval(approval.Value));
            KnownBackgroundProgram? known = TelemetryCatalog.MatchBackgroundProgram(entry.Name, entry.Command);
            SurveillanceLevel level = known?.Level ?? SurveillanceLevel.Unknown;
            bool canChange = approval != null && level != SurveillanceLevel.DoNotTouch;

            items.Add(new SurveillanceItem
            {
                Category = "Фоновые программы",
                Title = (known?.Title ?? entry.Name) + "  (" + entry.Command + ")",
                Details = (known?.Explanation ?? "Запускается вместе с Windows и работает в фоне. ASA не знает эту программу: если вы её не ставили, отключите.") + (disabled ? " Автозапуск отключён." : string.Empty),
                Level = disabled ? SurveillanceLevel.Harmless : level,
                Disabled = disabled,
                DisableLabel = canChange && !disabled ? "Отключить автозапуск" : null,
                Disable = canChange && !disabled ? () => WriteApproval(approval!.Value, true) : null,
                EnableLabel = canChange && disabled ? "Включить автозапуск обратно" : null,
                Enable = canChange && disabled ? () => WriteApproval(approval!.Value, false) : null
            });
        }
    }

    private static void CheckTraces(List<SurveillanceItem> items)
    {
        string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (Directory.Exists(recent))
        {
            var options = new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            int count = Directory.EnumerateFiles(recent, "*", options).Count(file => !file.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                items.Add(new SurveillanceItem
                {
                    Category = "Следы",
                    Title = $"Список недавних файлов: {count}",
                    Details = "Windows помнит, какие файлы и папки вы открывали. Это видит любой, кто сядет за компьютер, и любая программа.",
                    Level = count > 50 ? SurveillanceLevel.Recommended : SurveillanceLevel.Harmless,
                    DisableLabel = "Очистить список (вернуть нельзя)",
                    Disable = () =>
                    {
                        int deleted = 0;
                        foreach (string file in Directory.EnumerateFiles(recent, "*", options))
                        {
                            try
                            {
                                File.Delete(file);
                                deleted++;
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                            {
                            }
                        }

                        return "Удалено записей: " + deleted;
                    }
                });
            }
        }

        const string clipboardPath = @"Software\Microsoft\Clipboard";
        using (RegistryKey? clipboard = Registry.CurrentUser.OpenSubKey(clipboardPath))
        {
            bool history = clipboard?.GetValue("EnableClipboardHistory") is int historyValue && historyValue == 1;
            bool cloud = clipboard?.GetValue("EnableCloudClipboard") is int cloudValue && cloudValue == 1;
            if (history || cloud)
            {
                items.Add(new SurveillanceItem
                {
                    Category = "Следы",
                    Title = cloud ? "Буфер обмена отправляется в облако Microsoft" : "Windows хранит историю буфера обмена",
                    Details = cloud
                        ? "Всё, что вы копируете (включая пароли), уходит в Microsoft для синхронизации между устройствами."
                        : "Всё, что вы копировали (включая пароли), хранится и доступно по Win+V.",
                    Level = cloud ? SurveillanceLevel.Danger : SurveillanceLevel.Recommended,
                    DisableLabel = "Выключить историю и синхронизацию буфера",
                    Disable = () =>
                    {
                        using RegistryKey key = Registry.CurrentUser.CreateSubKey(clipboardPath);
                        key.SetValue("EnableClipboardHistory", 0, RegistryValueKind.DWord);
                        key.SetValue("EnableCloudClipboard", 0, RegistryValueKind.DWord);
                        return "История и синхронизация буфера выключены.";
                    }
                });
            }
        }

        string powerShellHistory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "PowerShell", "PSReadLine", "ConsoleHost_history.txt");
        if (File.Exists(powerShellHistory) && new FileInfo(powerShellHistory).Length > 0)
        {
            items.Add(new SurveillanceItem
            {
                Category = "Следы",
                Title = "История команд PowerShell",
                Details = "Все команды, которые вы вводили, лежат открытым текстом в файле. Если среди них были пароли или ключи, их может прочитать любая программа.",
                Level = SurveillanceLevel.Unknown,
                DisableLabel = "Очистить историю (вернуть нельзя)",
                Disable = () =>
                {
                    File.WriteAllText(powerShellHistory, string.Empty);
                    return "История команд очищена.";
                }
            });
        }
    }

    private static (RegistryHive Hive, string Key, string ValueName)? FindApprovalLocation(AutorunEntry entry)
    {
        const string root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        if (entry.Location.Contains("RunOnce", StringComparison.Ordinal))
        {
            return null;
        }

        return entry.Location switch
        {
            "Реестр пользователя: Run" => (RegistryHive.CurrentUser, root + "Run", entry.Name),
            "Реестр системы: Run" => (RegistryHive.LocalMachine, root + "Run", entry.Name),
            "Реестр системы (32 бит): Run" => (RegistryHive.LocalMachine, root + "Run32", entry.Name),
            "Папка автозагрузки пользователя" => (RegistryHive.CurrentUser, root + "StartupFolder", entry.Name),
            "Папка автозагрузки системы" => (RegistryHive.LocalMachine, root + "StartupFolder", entry.Name),
            _ => null
        };
    }

    private static byte[]? ReadApproval((RegistryHive Hive, string Key, string ValueName) location)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(location.Hive, RegistryView.Registry64);
        using RegistryKey? key = baseKey.OpenSubKey(location.Key);
        return key?.GetValue(location.ValueName) as byte[];
    }

    private static string WriteApproval((RegistryHive Hive, string Key, string ValueName) location, bool disabled)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(location.Hive, RegistryView.Registry64);
        using RegistryKey key = baseKey.CreateSubKey(location.Key, true);
        key.SetValue(location.ValueName, StartupApproval.CreateValue(disabled), RegistryValueKind.Binary);
        return disabled ? "Автозапуск отключён. Сама программа не удалена." : "Автозапуск включён.";
    }

    private static string SetConsent(string path, string value, string message)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(path, true);
        key.SetValue("Value", value, RegistryValueKind.String);
        return message;
    }

    private string DisableService(string name)
    {
        Dictionary<string, int> backup = LoadServicesBackup();
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(ServicesPath + "\\" + name, true))
        {
            if (key == null)
            {
                throw new InvalidOperationException("Служба не найдена.");
            }

            if (!backup.ContainsKey(name) && key.GetValue("Start") is int current)
            {
                backup[name] = current;
                File.WriteAllText(servicesBackupFile, JsonSerializer.Serialize(backup));
            }

            key.SetValue("Start", 4, RegistryValueKind.DWord);
        }

        EncryptedDnsService.TryRun("sc.exe", $"stop \"{name}\"");
        return "Служба остановлена и отключена.";
    }

    private string EnableService(string name, int previousStart)
    {
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(ServicesPath + "\\" + name, true))
        {
            key?.SetValue("Start", previousStart, RegistryValueKind.DWord);
        }

        Dictionary<string, int> backup = LoadServicesBackup();
        backup.Remove(name);
        File.WriteAllText(servicesBackupFile, JsonSerializer.Serialize(backup));
        return "Служба включена. Заработает после перезагрузки.";
    }

    private Dictionary<string, int> LoadServicesBackup()
    {
        if (!File.Exists(servicesBackupFile))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            Dictionary<string, int>? loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(servicesBackupFile));
            return new Dictionary<string, int>(loaded ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string BlockProgram(string path)
    {
        string name = ProgramRulePrefix + path;
        RunTool("netsh.exe", $"advfirewall firewall add rule name=\"{name}\" dir=out action=block program=\"{path}\" enable=yes", string.Empty);
        RunTool("netsh.exe", $"advfirewall firewall add rule name=\"{name}\" dir=in action=block program=\"{path}\" enable=yes", string.Empty);
        return "Программе запрещён выход в интернет.";
    }

    private static string BlockPort(int port)
    {
        return RunTool("netsh.exe", $"advfirewall firewall add rule name=\"{PortRulePrefix}{port}\" dir=in action=block protocol=TCP localport={port} enable=yes", "Порт закрыт в брандмауэре.");
    }

    private static string RemoveFirewallRule(string name)
    {
        return RunTool("netsh.exe", $"advfirewall firewall delete rule name=\"{name}\"", "Правило брандмауэра удалено.");
    }

    private static HashSet<string> ReadFirewallRuleNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using RegistryKey? rules = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules");
        if (rules == null)
        {
            return names;
        }

        foreach (string valueName in rules.GetValueNames())
        {
            string rule = rules.GetValue(valueName) as string ?? string.Empty;
            foreach (string part in rule.Split('|'))
            {
                if (part.StartsWith("Name=ASA block", StringComparison.Ordinal))
                {
                    names.Add(part.Substring("Name=".Length));
                }
            }
        }

        return names;
    }

    private static (string Name, string? Path) DescribeProcess(int processId)
    {
        if (processId == 0)
        {
            return ("Idle", null);
        }

        if (processId == 4)
        {
            return ("System", null);
        }

        string? path = NativeMethods.GetProcessPath(processId);
        try
        {
            using Process process = Process.GetProcessById(processId);
            return (process.ProcessName, path);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return (path != null ? Path.GetFileNameWithoutExtension(path) : "процесс " + processId, path);
        }
    }

    private static HashSet<string> LoadTrackerDomains()
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            bool inside = false;
            foreach (string rawLine in File.ReadLines(WebShield.HostsFilePath))
            {
                string line = rawLine.Trim();
                if (line == HostsBlocker.TrackerBeginMarker)
                {
                    inside = true;
                }
                else if (line == HostsBlocker.TrackerEndMarker)
                {
                    inside = false;
                }
                else if (inside)
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        domains.Add(parts[1]);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return domains;
    }

    private static Dictionary<string, string> ResolveHostNames(List<IPAddress> addresses)
    {
        var names = new Dictionary<string, string>();
        foreach ((string address, string host) in ReadDnsCache())
        {
            names[address] = host;
        }

        List<IPAddress> unresolved = addresses.Where(address => !names.ContainsKey(address.ToString())).Take(60).ToList();
        var lookups = unresolved.Select(async address =>
        {
            try
            {
                IPHostEntry entry = await Dns.GetHostEntryAsync(address).WaitAsync(TimeSpan.FromSeconds(3));
                return (Address: address.ToString(), Host: entry.HostName);
            }
            catch (Exception exception) when (exception is System.Net.Sockets.SocketException or TimeoutException or ArgumentException)
            {
                return (Address: address.ToString(), Host: string.Empty);
            }
        }).ToArray();

        try
        {
            Task.WaitAll(lookups, TimeSpan.FromSeconds(6));
        }
        catch (AggregateException)
        {
        }

        foreach (var lookup in lookups.Where(task => task.IsCompletedSuccessfully))
        {
            if (lookup.Result.Host.Length > 0 && !IPAddress.TryParse(lookup.Result.Host, out _))
            {
                names[lookup.Result.Address] = lookup.Result.Host;
            }
        }

        return names;
    }

    private static List<(string Address, string Host)> ReadDnsCache()
    {
        var results = new List<(string Address, string Host)>();
        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"Get-DnsClientCache | Where-Object { $_.Data } | ForEach-Object { $_.Entry + '|' + $_.Data }\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                return results;
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                process.Kill(true);
                return results;
            }

            foreach (string line in output.Result.Split('\n'))
            {
                string[] parts = line.Trim().Split('|');
                if (parts.Length == 2 && IPAddress.TryParse(parts[1], out IPAddress? address))
                {
                    results.Add((address.ToString(), parts[0]));
                }
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or AggregateException)
        {
        }

        return results;
    }

    private static string RunTool(string fileName, string arguments, string successMessage)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить " + fileName);
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(output.Trim().Length > 0 ? output.Trim() : fileName + " завершился с кодом " + process.ExitCode);
        }

        return successMessage;
    }
}
