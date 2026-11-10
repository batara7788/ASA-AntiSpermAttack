using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.Json;
using Asa.Core;
using Microsoft.Win32;

namespace Asa.App.Services;

public sealed class SystemAuditor
{
    private readonly Quarantine quarantine;

    public SystemAuditor(Quarantine quarantine)
    {
        this.quarantine = quarantine;
    }

    public List<SecurityFinding> Run(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var findings = new List<SecurityFinding>();
        var steps = new (string Name, Action<List<SecurityFinding>> Check)[]
        {
            ("Защитник Windows", CheckDefender),
            ("Контроль учётных записей и брандмауэр", CheckSystemSettings),
            ("Перехват запуска программ", CheckExecutionHijacks),
            ("Файл hosts", CheckHosts),
            ("Прокси", CheckProxy),
            ("Запланированные задачи", CheckScheduledTasks),
            ("Службы", CheckServices),
            ("Ярлыки", CheckShortcuts),
            ("Подписки WMI", CheckWmiSubscriptions),
            ("Расширения браузеров", CheckBrowserExtensions)
        };

        foreach ((string name, Action<List<SecurityFinding>> check) in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report("Аудит: " + name);
            try
            {
                check(findings);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                findings.Add(new SecurityFinding
                {
                    Category = name,
                    Title = "Не удалось проверить",
                    Details = exception.Message,
                    Severity = FindingSeverity.Info,
                    TrustKey = "error|" + name
                });
            }
        }

        return findings;
    }

    private static void CheckDefender(List<SecurityFinding> findings)
    {
        DefenderStatus status = DefenderEngine.ReadStatus();
        if (!status.Installed)
        {
            findings.Add(new SecurityFinding
            {
                Category = "Защитник Windows",
                Title = "Защитник Windows не найден",
                Details = "Второй движок проверки недоступен. Если стоит другой антивирус, это нормально.",
                Severity = FindingSeverity.Info,
                TrustKey = "defender|missing"
            });
            return;
        }

        if (!status.RealTimeEnabled)
        {
            findings.Add(new SecurityFinding
            {
                Category = "Защитник Windows",
                Title = "Защита в реальном времени Защитника выключена",
                Details = "Вирусы часто отключают Защитник первым делом. Если вы не выключали его сами, это тревожный знак.",
                Severity = FindingSeverity.Danger,
                FixDescription = "Включить защиту Защитника",
                Fix = () => RunPowerShell("Set-MpPreference -DisableRealtimeMonitoring $false"),
                TrustKey = "defender|realtime"
            });
        }

        foreach (string path in status.ExcludedPaths)
        {
            AddDefenderExclusion(findings, "ExclusionPath", path, "папка или файл");
        }

        foreach (string process in status.ExcludedProcesses)
        {
            AddDefenderExclusion(findings, "ExclusionProcess", process, "процесс");
        }

        foreach (string extension in status.ExcludedExtensions)
        {
            AddDefenderExclusion(findings, "ExclusionExtension", extension, "расширение");
        }
    }

    private static void AddDefenderExclusion(List<SecurityFinding> findings, string kind, string value, string description)
    {
        string lower = value.ToLowerInvariant();
        bool broad = lower.Length <= 3 || lower is "c:\\" or "c:" or ".exe" or "exe" or ".dll" or ".ps1" || lower.Contains("\\temp") || lower.Contains("\\appdata") || lower.Contains("\\users\\public");
        findings.Add(new SecurityFinding
        {
            Category = "Защитник Windows",
            Title = $"Исключение в Защитнике ({description}): {value}",
            Details = broad
                ? "Слишком широкое исключение: всё внутри Защитник не проверяет. Так прячутся вирусы."
                : "Защитник это не проверяет. Если исключение добавляли не вы, его стоит убрать.",
            Severity = broad ? FindingSeverity.Danger : FindingSeverity.Warning,
            FixDescription = "Убрать исключение",
            Fix = () => RunPowerShell($"Remove-MpPreference -{kind} '{value.Replace("'", "''")}'"),
            TrustKey = "defender|" + kind + "|" + value
        });
    }

    private static void CheckSystemSettings(List<SecurityFinding> findings)
    {
        const string policies = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        object? enableLua = Registry.LocalMachine.OpenSubKey(policies)?.GetValue("EnableLUA");
        if (enableLua is int luaValue && luaValue == 0)
        {
            findings.Add(new SecurityFinding
            {
                Category = "Система",
                Title = "Контроль учётных записей (UAC) выключен",
                Details = "Любая программа получает права администратора без вопросов.",
                Severity = FindingSeverity.Danger,
                FixDescription = "Включить UAC (нужна перезагрузка)",
                Fix = () =>
                {
                    using RegistryKey key = Registry.LocalMachine.CreateSubKey(policies);
                    key.SetValue("EnableLUA", 1, RegistryValueKind.DWord);
                    return "UAC включён. Перезагрузите компьютер.";
                },
                TrustKey = "uac|off"
            });
        }

        object? consent = Registry.LocalMachine.OpenSubKey(policies)?.GetValue("ConsentPromptBehaviorAdmin");
        if (enableLua is not 0 && consent is int consentValue && consentValue == 0)
        {
            findings.Add(new SecurityFinding
            {
                Category = "Система",
                Title = "UAC повышает права без запроса",
                Details = "Программы получают права администратора молча.",
                Severity = FindingSeverity.Warning,
                FixDescription = "Вернуть запрос подтверждения",
                Fix = () =>
                {
                    using RegistryKey key = Registry.LocalMachine.CreateSubKey(policies);
                    key.SetValue("ConsentPromptBehaviorAdmin", 5, RegistryValueKind.DWord);
                    return "Запрос подтверждения UAC возвращён.";
                },
                TrustKey = "uac|silent"
            });
        }

        const string firewallRoot = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
        var disabledProfiles = new List<string>();
        foreach ((string profileKey, string profileName) in new[] { ("DomainProfile", "доменная"), ("StandardProfile", "частная"), ("PublicProfile", "общественная") })
        {
            object? enabled = Registry.LocalMachine.OpenSubKey(firewallRoot + profileKey)?.GetValue("EnableFirewall");
            if (enabled is int value && value == 0)
            {
                disabledProfiles.Add(profileName);
            }
        }

        if (disabledProfiles.Count > 0)
        {
            findings.Add(new SecurityFinding
            {
                Category = "Система",
                Title = "Брандмауэр Windows выключен: " + string.Join(", ", disabledProfiles) + " сеть",
                Details = "Компьютер открыт для входящих подключений из сети.",
                Severity = disabledProfiles.Contains("общественная") ? FindingSeverity.Danger : FindingSeverity.Warning,
                FixDescription = "Включить брандмауэр",
                Fix = () => RunProcess("netsh", "advfirewall set allprofiles state on"),
                TrustKey = "firewall|off"
            });
        }
    }

    private static void CheckExecutionHijacks(List<SecurityFinding> findings)
    {
        const string ifeoPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        using (RegistryKey? ifeo = Registry.LocalMachine.OpenSubKey(ifeoPath))
        {
            if (ifeo != null)
            {
                foreach (string programName in ifeo.GetSubKeyNames())
                {
                    using RegistryKey? programKey = ifeo.OpenSubKey(programName);
                    if (programKey?.GetValue("Debugger") is string debugger && debugger.Trim().Length > 0)
                    {
                        string capturedName = programName;
                        findings.Add(new SecurityFinding
                        {
                            Category = "Перехват запуска",
                            Title = $"Запуск «{programName}» перехвачен",
                            Details = $"Вместо {programName} запускается: {debugger}. Так вирусы блокируют антивирусы и диспетчер задач.",
                            Severity = FindingSeverity.Danger,
                            FixDescription = "Убрать перехват",
                            Fix = () =>
                            {
                                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ifeoPath + "\\" + capturedName, true);
                                key?.DeleteValue("Debugger", false);
                                return "Перехват запуска убран.";
                            },
                            TrustKey = "ifeo|" + programName + "|" + debugger
                        });
                    }
                }
            }
        }

        const string winlogonPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        using (RegistryKey? winlogon = Registry.LocalMachine.OpenSubKey(winlogonPath))
        {
            string shell = winlogon?.GetValue("Shell") as string ?? "explorer.exe";
            if (!string.Equals(shell.Trim(), "explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new SecurityFinding
                {
                    Category = "Перехват запуска",
                    Title = "Подменена оболочка Windows",
                    Details = "При входе вместо explorer.exe запускается: " + shell,
                    Severity = FindingSeverity.Danger,
                    FixDescription = "Вернуть explorer.exe",
                    Fix = () => SetWinlogonValue(winlogonPath, "Shell", "explorer.exe"),
                    TrustKey = "winlogon|shell|" + shell
                });
            }

            string userinit = winlogon?.GetValue("Userinit") as string ?? string.Empty;
            string normalized = userinit.Trim().TrimEnd(',').ToLowerInvariant();
            if (userinit.Length > 0 && !normalized.EndsWith("\\system32\\userinit.exe", StringComparison.Ordinal))
            {
                findings.Add(new SecurityFinding
                {
                    Category = "Перехват запуска",
                    Title = "Подменён Userinit",
                    Details = "При входе в Windows дополнительно запускается: " + userinit,
                    Severity = FindingSeverity.Danger,
                    FixDescription = "Вернуть стандартное значение",
                    Fix = () => SetWinlogonValue(winlogonPath, "Userinit", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "userinit.exe") + ","),
                    TrustKey = "winlogon|userinit|" + userinit
                });
            }
        }

        const string windowsPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows";
        using RegistryKey? windowsKey = Registry.LocalMachine.OpenSubKey(windowsPath);
        if (windowsKey?.GetValue("AppInit_DLLs") is string appInit && appInit.Trim().Length > 0 && windowsKey.GetValue("LoadAppInit_DLLs") is int load && load == 1)
        {
            findings.Add(new SecurityFinding
            {
                Category = "Перехват запуска",
                Title = "Библиотеки внедряются в каждую программу (AppInit_DLLs)",
                Details = appInit,
                Severity = FindingSeverity.Danger,
                FixDescription = "Отключить AppInit_DLLs",
                Fix = () =>
                {
                    using RegistryKey? key = Registry.LocalMachine.OpenSubKey(windowsPath, true);
                    key?.SetValue("LoadAppInit_DLLs", 0, RegistryValueKind.DWord);
                    return "AppInit_DLLs отключены.";
                },
                TrustKey = "appinit|" + appInit
            });
        }
    }

    private static void CheckHosts(List<SecurityFinding> findings)
    {
        string hostsPath = WebShield.HostsFilePath;
        if (!File.Exists(hostsPath))
        {
            return;
        }

        string text = File.ReadAllText(hostsPath);
        foreach ((string address, string domain) in HostsAudit.FindSuspiciousRedirects(text))
        {
            string capturedDomain = domain;
            findings.Add(new SecurityFinding
            {
                Category = "Файл hosts",
                Title = $"Сайт {domain} перенаправлен на {address}",
                Details = "Так вирусы подсовывают поддельные сайты банков и почты или блокируют антивирусы.",
                Severity = FindingSeverity.Danger,
                FixDescription = "Удалить запись из hosts",
                Fix = () =>
                {
                    string current = File.ReadAllText(hostsPath);
                    File.WriteAllText(hostsPath, HostsAudit.RemoveEntries(current, new[] { capturedDomain }), new UTF8Encoding(false));
                    try
                    {
                        RunProcess("ipconfig", "/flushdns");
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                    }

                    return "Запись удалена.";
                },
                TrustKey = "hosts|" + domain + "|" + address
            });
        }
    }

    private static void CheckProxy(List<SecurityFinding> findings)
    {
        const string settingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey(settingsPath);
        if (settings == null)
        {
            return;
        }

        if (settings.GetValue("ProxyEnable") is int enabled && enabled == 1 && settings.GetValue("ProxyServer") is string server)
        {
            findings.Add(new SecurityFinding
            {
                Category = "Прокси",
                Title = "Весь интернет идёт через прокси " + server,
                Details = "Если прокси настраивали не вы, кто-то может читать и подменять ваш трафик.",
                Severity = FindingSeverity.Warning,
                FixDescription = "Выключить прокси",
                Fix = () =>
                {
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(settingsPath);
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    return "Прокси выключен.";
                },
                TrustKey = "proxy|" + server
            });
        }

        if (settings.GetValue("AutoConfigURL") is string configUrl && configUrl.Trim().Length > 0)
        {
            findings.Add(new SecurityFinding
            {
                Category = "Прокси",
                Title = "Задан сценарий автонастройки прокси",
                Details = configUrl + ". Через такой сценарий вирусы направляют отдельные сайты через свой сервер.",
                Severity = FindingSeverity.Warning,
                FixDescription = "Убрать сценарий",
                Fix = () =>
                {
                    using RegistryKey key = Registry.CurrentUser.CreateSubKey(settingsPath);
                    key.DeleteValue("AutoConfigURL", false);
                    return "Сценарий автонастройки убран.";
                },
                TrustKey = "pac|" + configUrl
            });
        }
    }

    private void CheckScheduledTasks(List<SecurityFinding> findings)
    {
        string tasksFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
        if (!Directory.Exists(tasksFolder))
        {
            return;
        }

        var options = new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (string taskFile in Directory.EnumerateFiles(tasksFolder, "*", options))
        {
            string taskName = "\\" + Path.GetRelativePath(tasksFolder, taskFile);
            if (taskName.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string xml;
            try
            {
                xml = File.ReadAllText(taskFile);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (ScheduledTaskAction action in ScheduledTaskInspector.ParseTaskXml(taskName, xml))
            {
                IReadOnlyList<RiskReason> reasons = ScheduledTaskInspector.Assess(action);
                string executable = Environment.ExpandEnvironmentVariables(action.Command);
                if (File.Exists(executable) && FileRiskAnalyzer.IsExecutableLike(executable) && SignatureVerifier.HasValidSignature(executable) == false)
                {
                    reasons = reasons.Append(new RiskReason(10, "Программа без цифровой подписи")).ToList();
                }

                int score = reasons.Sum(reason => reason.Points);
                if (score < 30)
                {
                    continue;
                }

                string capturedName = taskName;
                findings.Add(new SecurityFinding
                {
                    Category = "Запланированные задачи",
                    Title = "Подозрительная задача " + taskName,
                    Details = $"{action.Command} {action.Arguments}".Trim() + ". " + string.Join("; ", reasons.Select(reason => reason.Description)),
                    Severity = score >= 50 ? FindingSeverity.Danger : FindingSeverity.Warning,
                    FilePath = File.Exists(executable) ? executable : null,
                    FixDescription = "Удалить задачу",
                    Fix = () => RunProcess("schtasks", $"/Delete /TN \"{capturedName}\" /F"),
                    TrustKey = "task|" + taskName + "|" + action.Command + "|" + action.Arguments
                });
            }
        }
    }

    private static void CheckServices(List<SecurityFinding> findings)
    {
        const string servicesPath = @"SYSTEM\CurrentControlSet\Services";
        using RegistryKey? services = Registry.LocalMachine.OpenSubKey(servicesPath);
        if (services == null)
        {
            return;
        }

        foreach (string serviceName in services.GetSubKeyNames())
        {
            using RegistryKey? service = services.OpenSubKey(serviceName);
            if (service?.GetValue("ImagePath") is not string imagePath || service.GetValue("Type") is not int type || (type & 0x30) == 0)
            {
                continue;
            }

            if (service.GetValue("Start") is not int start || start > 2)
            {
                continue;
            }

            string expanded = Environment.ExpandEnvironmentVariables(imagePath);
            string lower = expanded.ToLowerInvariant();
            var reasons = new List<string>();
            if (lower.Contains("\\appdata\\") || lower.Contains("\\temp\\") || lower.Contains("\\users\\public\\") || (lower.Contains("\\programdata\\") && !lower.Contains("\\microsoft\\")))
            {
                reasons.Add("программа службы лежит в пользовательской или временной папке");
            }

            IReadOnlyList<string> markers = ShortcutInspector.FindDangerousMarkers(expanded);
            if (markers.Count > 0)
            {
                reasons.Add("служба запускает команды: " + string.Join(", ", markers.Take(3)));
            }

            string? executable = AutorunSnapshot.ExtractExecutablePath(expanded);
            if (executable != null && File.Exists(executable) && !lower.Contains("\\windows\\") && SignatureVerifier.HasValidSignature(executable) == false)
            {
                reasons.Add("программа службы без цифровой подписи");
            }

            if (reasons.Count == 0 || (reasons.Count == 1 && reasons[0].Contains("подписи")))
            {
                continue;
            }

            string capturedName = serviceName;
            findings.Add(new SecurityFinding
            {
                Category = "Службы",
                Title = "Подозрительная служба " + serviceName,
                Details = imagePath + ". " + string.Join("; ", reasons),
                Severity = markers.Count > 0 ? FindingSeverity.Danger : FindingSeverity.Warning,
                FilePath = executable != null && File.Exists(executable) ? executable : null,
                FixDescription = "Отключить службу",
                Fix = () =>
                {
                    using RegistryKey? key = Registry.LocalMachine.OpenSubKey(servicesPath + "\\" + capturedName, true);
                    key?.SetValue("Start", 4, RegistryValueKind.DWord);
                    RunProcess("sc", $"stop \"{capturedName}\"");
                    return "Служба остановлена и отключена.";
                },
                TrustKey = "service|" + serviceName + "|" + imagePath
            });
        }
    }

    private void CheckShortcuts(List<SecurityFinding> findings)
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Path.Combine(roaming, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar")
        };

        var options = new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 };
        foreach (string folder in folders.Where(folder => folder.Length > 0 && Directory.Exists(folder)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string shortcut in Directory.EnumerateFiles(folder, "*.lnk", options))
            {
                byte[] content;
                try
                {
                    content = File.ReadAllBytes(shortcut);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                IReadOnlyList<string> markers = ShortcutInspector.InspectShortcut(content);
                bool onlyWebLink = markers.All(marker => marker.StartsWith("http", StringComparison.Ordinal));
                if (markers.Count == 0 || onlyWebLink)
                {
                    continue;
                }

                string capturedShortcut = shortcut;
                findings.Add(new SecurityFinding
                {
                    Category = "Ярлыки",
                    Title = "Ярлык запускает скрытые команды: " + Path.GetFileName(shortcut),
                    Details = shortcut + ". Внутри: " + string.Join(", ", markers.Take(5)),
                    Severity = markers.Any(marker => marker.Contains("hidden") || marker.Contains("enc") || marker.Contains("base64") || marker.Contains("download")) ? FindingSeverity.Danger : FindingSeverity.Warning,
                    FilePath = shortcut,
                    FixDescription = "Убрать ярлык в карантин",
                    Fix = () =>
                    {
                        quarantine.Add(capturedShortcut, "Подозрительный ярлык");
                        return "Ярлык в карантине.";
                    },
                    TrustKey = "shortcut|" + shortcut
                });
            }
        }
    }

    private static void CheckWmiSubscriptions(List<SecurityFinding> findings)
    {
        var scope = new ManagementScope(@"\\.\root\subscription");
        foreach ((string className, string field) in new[] { ("CommandLineEventConsumer", "CommandLineTemplate"), ("ActiveScriptEventConsumer", "ScriptText") })
        {
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM " + className));
            ManagementObjectCollection results;
            try
            {
                results = searcher.Get();
            }
            catch (ManagementException)
            {
                continue;
            }

            foreach (ManagementObject consumer in results.Cast<ManagementObject>())
            {
                string name = consumer["Name"]?.ToString() ?? "(без имени)";
                string body = consumer[field]?.ToString() ?? string.Empty;
                if (name.Equals("SCM Event Log Consumer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string capturedPath = consumer.Path.Path;
                findings.Add(new SecurityFinding
                {
                    Category = "Подписки WMI",
                    Title = "Скрытый автозапуск через WMI: " + name,
                    Details = (body.Length > 300 ? body.Substring(0, 300) + "…" : body) + ". Обычные программы так почти не делают, а вирусы прячутся здесь, потому что в автозагрузке этого не видно.",
                    Severity = FindingSeverity.Danger,
                    FixDescription = "Удалить подписку",
                    Fix = () =>
                    {
                        using var target = new ManagementObject(capturedPath);
                        target.Delete();
                        return "Подписка WMI удалена.";
                    },
                    TrustKey = "wmi|" + className + "|" + name
                });
            }
        }
    }

    private static void CheckBrowserExtensions(List<SecurityFinding> findings)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var browsers = new[]
        {
            ("Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Яндекс Браузер", Path.Combine(local, "Yandex", "YandexBrowser", "User Data"))
        };

        string[] powerfulPermissions = { "webRequest", "webRequestBlocking", "proxy", "debugger", "nativeMessaging", "cookies", "management", "declarativeNetRequest" };

        foreach ((string browser, string userData) in browsers)
        {
            if (!Directory.Exists(userData))
            {
                continue;
            }

            foreach (string profile in Directory.EnumerateDirectories(userData))
            {
                string extensionsFolder = Path.Combine(profile, "Extensions");
                if (!Directory.Exists(extensionsFolder))
                {
                    continue;
                }

                foreach (string extensionFolder in Directory.EnumerateDirectories(extensionsFolder))
                {
                    string? manifestPath = Directory.EnumerateDirectories(extensionFolder)
                        .Select(version => Path.Combine(version, "manifest.json"))
                        .FirstOrDefault(File.Exists);
                    if (manifestPath == null)
                    {
                        continue;
                    }

                    try
                    {
                        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                        JsonElement root = manifest.RootElement;
                        var permissions = new List<string>();
                        foreach (string property in new[] { "permissions", "host_permissions", "optional_permissions" })
                        {
                            if (root.TryGetProperty(property, out JsonElement list) && list.ValueKind == JsonValueKind.Array)
                            {
                                permissions.AddRange(list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!));
                            }
                        }

                        bool allSites = permissions.Any(permission => permission is "<all_urls>" or "*://*/*" or "http://*/*" or "https://*/*");
                        List<string> powerful = permissions.Intersect(powerfulPermissions).ToList();
                        string updateUrl = root.TryGetProperty("update_url", out JsonElement update) ? update.GetString() ?? string.Empty : string.Empty;
                        bool fromStore = updateUrl.Contains("google.com", StringComparison.OrdinalIgnoreCase) || updateUrl.Contains("microsoft.com", StringComparison.OrdinalIgnoreCase) || updateUrl.Contains("yandex", StringComparison.OrdinalIgnoreCase);

                        if (!(allSites && powerful.Count > 0) && fromStore)
                        {
                            continue;
                        }

                        if (!allSites && powerful.Count == 0)
                        {
                            continue;
                        }

                        string name = root.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                        if (name.StartsWith("__MSG_", StringComparison.Ordinal))
                        {
                            name = Path.GetFileName(extensionFolder);
                        }

                        findings.Add(new SecurityFinding
                        {
                            Category = "Расширения браузеров",
                            Title = $"{browser}: расширение «{name}» видит все сайты",
                            Details = (fromStore ? "Из магазина расширений. " : "Установлено НЕ из магазина. ") +
                                      "Права: " + string.Join(", ", powerful.Prepend(allSites ? "все сайты" : string.Empty).Where(item => item.Length > 0)) +
                                      ". Такое расширение может читать пароли и подменять страницы. Если вы его не ставили, удалите в настройках браузера.",
                            Severity = fromStore ? FindingSeverity.Info : FindingSeverity.Warning,
                            FilePath = manifestPath,
                            TrustKey = "extension|" + browser + "|" + Path.GetFileName(extensionFolder)
                        });
                    }
                    catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                    }
                }
            }
        }
    }

    private static string SetWinlogonValue(string path, string name, string value)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.String);
        return $"{name} восстановлен. Перезагрузите компьютер.";
    }

    private static string RunPowerShell(string command)
    {
        return RunProcess("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"");
    }

    private static string RunProcess(string fileName, string arguments)
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
        process.WaitForExit(60000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(output.Trim().Length > 0 ? output.Trim() : fileName + " завершился с кодом " + process.ExitCode);
        }

        return "Готово.";
    }
}
