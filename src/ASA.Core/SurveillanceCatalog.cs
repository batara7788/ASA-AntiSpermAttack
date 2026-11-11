using System.Xml.Linq;

namespace Asa.Core;

public enum SurveillanceLevel
{
    Harmless,
    DoNotTouch,
    Unknown,
    Recommended,
    Danger
}

public enum HostKind
{
    Unknown,
    Normal,
    SystemUpdate,
    CrashReports,
    Telemetry,
    Advertising
}

public sealed record HostOwner(string Owner, HostKind Kind);

public static class HostClassifier
{
    private static readonly (string Suffix, string Owner, HostKind Kind)[] Rules =
    {
        ("doubleclick.net", "Google (реклама)", HostKind.Advertising),
        ("googlesyndication.com", "Google (реклама)", HostKind.Advertising),
        ("googleadservices.com", "Google (реклама)", HostKind.Advertising),
        ("google-analytics.com", "Google Analytics", HostKind.Advertising),
        ("googletagmanager.com", "Google Tag Manager", HostKind.Advertising),
        ("app-measurement.com", "Google Firebase Analytics", HostKind.Advertising),
        ("facebook.net", "Meta (трекер)", HostKind.Advertising),
        ("graph.facebook.com", "Meta (трекер)", HostKind.Advertising),
        ("mc.yandex.ru", "Яндекс Метрика", HostKind.Advertising),
        ("an.yandex.ru", "Яндекс (реклама)", HostKind.Advertising),
        ("adfox.ru", "Яндекс (реклама)", HostKind.Advertising),
        ("top-fwz1.mail.ru", "VK (счётчик)", HostKind.Advertising),
        ("ad.mail.ru", "VK (реклама)", HostKind.Advertising),
        ("scorecardresearch.com", "Comscore (трекер)", HostKind.Advertising),
        ("appsflyer.com", "AppsFlyer (трекер)", HostKind.Advertising),
        ("adjust.com", "Adjust (трекер)", HostKind.Advertising),
        ("mixpanel.com", "Mixpanel (аналитика)", HostKind.Advertising),
        ("amplitude.com", "Amplitude (аналитика)", HostKind.Advertising),
        ("segment.io", "Segment (аналитика)", HostKind.Advertising),
        ("criteo.com", "Criteo (реклама)", HostKind.Advertising),
        ("adnxs.com", "Xandr (реклама)", HostKind.Advertising),
        ("taboola.com", "Taboola (реклама)", HostKind.Advertising),
        ("hotjar.com", "Hotjar (запись действий)", HostKind.Advertising),
        ("events.data.microsoft.com", "Microsoft (телеметрия)", HostKind.Telemetry),
        ("vortex.data.microsoft.com", "Microsoft (телеметрия)", HostKind.Telemetry),
        ("settings-win.data.microsoft.com", "Microsoft (телеметрия)", HostKind.Telemetry),
        ("telemetry.microsoft.com", "Microsoft (телеметрия)", HostKind.Telemetry),
        ("telecommand.telemetry.microsoft.com", "Microsoft (телеметрия)", HostKind.Telemetry),
        ("browser.events.data.msn.com", "Microsoft Edge (телеметрия)", HostKind.Telemetry),
        ("activity.windows.com", "Microsoft (журнал действий)", HostKind.Telemetry),
        ("telemetry.mozilla.org", "Mozilla (телеметрия)", HostKind.Telemetry),
        ("events.gfe.nvidia.com", "NVIDIA (телеметрия)", HostKind.Telemetry),
        ("gfe.nvidia.com", "NVIDIA (телеметрия)", HostKind.Telemetry),
        ("cc-api-data.adobe.io", "Adobe (телеметрия)", HostKind.Telemetry),
        ("adobedc.net", "Adobe (телеметрия)", HostKind.Telemetry),
        ("watson.microsoft.com", "Microsoft (отчёты об ошибках)", HostKind.CrashReports),
        ("watson.telemetry.microsoft.com", "Microsoft (отчёты об ошибках)", HostKind.CrashReports),
        ("sentry.io", "Sentry (отчёты об ошибках)", HostKind.CrashReports),
        ("crashlytics.com", "Google Crashlytics", HostKind.CrashReports),
        ("bugsnag.com", "Bugsnag (отчёты об ошибках)", HostKind.CrashReports),
        ("windowsupdate.com", "Microsoft (обновления Windows)", HostKind.SystemUpdate),
        ("update.microsoft.com", "Microsoft (обновления Windows)", HostKind.SystemUpdate),
        ("delivery.mp.microsoft.com", "Microsoft (обновления Windows)", HostKind.SystemUpdate),
        ("wdcp.microsoft.com", "Защитник Windows", HostKind.SystemUpdate),
        ("smartscreen.microsoft.com", "Microsoft SmartScreen", HostKind.SystemUpdate),
        ("database.clamav.net", "ClamAV (вирусные базы)", HostKind.SystemUpdate),
        ("1e100.net", "Google", HostKind.Normal),
        ("google.com", "Google", HostKind.Normal),
        ("googleapis.com", "Google", HostKind.Normal),
        ("gstatic.com", "Google", HostKind.Normal),
        ("googleusercontent.com", "Google", HostKind.Normal),
        ("youtube.com", "YouTube", HostKind.Normal),
        ("googlevideo.com", "YouTube", HostKind.Normal),
        ("microsoft.com", "Microsoft", HostKind.Normal),
        ("live.com", "Microsoft", HostKind.Normal),
        ("office.com", "Microsoft", HostKind.Normal),
        ("office365.com", "Microsoft", HostKind.Normal),
        ("msedge.net", "Microsoft", HostKind.Normal),
        ("azure.com", "Microsoft Azure", HostKind.Normal),
        ("azureedge.net", "Microsoft Azure", HostKind.Normal),
        ("windows.net", "Microsoft Azure", HostKind.Normal),
        ("amazonaws.com", "Amazon (хостинг)", HostKind.Normal),
        ("cloudfront.net", "Amazon (сеть доставки)", HostKind.Normal),
        ("akamai.net", "Akamai (сеть доставки)", HostKind.Normal),
        ("akamaitechnologies.com", "Akamai (сеть доставки)", HostKind.Normal),
        ("akamaiedge.net", "Akamai (сеть доставки)", HostKind.Normal),
        ("cloudflare.com", "Cloudflare", HostKind.Normal),
        ("cloudflare.net", "Cloudflare", HostKind.Normal),
        ("fastly.net", "Fastly (сеть доставки)", HostKind.Normal),
        ("github.com", "GitHub", HostKind.Normal),
        ("githubusercontent.com", "GitHub", HostKind.Normal),
        ("discord.com", "Discord", HostKind.Normal),
        ("discord.gg", "Discord", HostKind.Normal),
        ("discord.media", "Discord", HostKind.Normal),
        ("steampowered.com", "Steam", HostKind.Normal),
        ("steamcontent.com", "Steam", HostKind.Normal),
        ("valve.net", "Steam", HostKind.Normal),
        ("telegram.org", "Telegram", HostKind.Normal),
        ("yandex.ru", "Яндекс", HostKind.Normal),
        ("yandex.net", "Яндекс", HostKind.Normal),
        ("vk.com", "VK", HostKind.Normal),
        ("userapi.com", "VK", HostKind.Normal),
        ("mail.ru", "VK", HostKind.Normal),
        ("apple.com", "Apple", HostKind.Normal),
        ("icloud.com", "Apple", HostKind.Normal),
        ("spotify.com", "Spotify", HostKind.Normal),
        ("quad9.net", "Quad9 (шифрованный DNS)", HostKind.Normal),
        ("adguard-dns.com", "AdGuard (шифрованный DNS)", HostKind.Normal)
    };

    public static HostOwner Classify(string? hostName, ISet<string>? trackerDomains = null)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return new HostOwner("неизвестный адрес", HostKind.Unknown);
        }

        string host = hostName.Trim().TrimEnd('.').ToLowerInvariant();
        (string Suffix, string Owner, HostKind Kind)? best = null;
        foreach ((string Suffix, string Owner, HostKind Kind) rule in Rules)
        {
            bool matches = host == rule.Suffix || host.EndsWith("." + rule.Suffix, StringComparison.Ordinal);
            if (matches && (best == null || rule.Suffix.Length > best.Value.Suffix.Length))
            {
                best = rule;
            }
        }

        if (best != null && best.Value.Kind != HostKind.Normal)
        {
            return new HostOwner(best.Value.Owner, best.Value.Kind);
        }

        if (trackerDomains != null && IsInDomainSet(host, trackerDomains))
        {
            return new HostOwner(host + " (в списке трекеров)", HostKind.Advertising);
        }

        if (best != null)
        {
            return new HostOwner(best.Value.Owner, best.Value.Kind);
        }

        return new HostOwner(host, HostKind.Unknown);
    }

    public static bool IsPublicAddress(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            bool isPrivate = bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || bytes[0] == 0
                || bytes[0] >= 224;
            return !isPrivate;
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC || address.Equals(System.Net.IPAddress.IPv6None));
    }

    private static bool IsInDomainSet(string host, ISet<string> domains)
    {
        string current = host;
        while (true)
        {
            if (domains.Contains(current))
            {
                return true;
            }

            int dot = current.IndexOf('.');
            if (dot < 0 || current.IndexOf('.', dot + 1) < 0)
            {
                return false;
            }

            current = current.Substring(dot + 1);
        }
    }
}

public sealed record PortInfo(string Name, string Explanation, SurveillanceLevel Level);

public static class PortCatalog
{
    private static readonly Dictionary<int, PortInfo> Known = new()
    {
        [21] = new PortInfo("FTP-сервер", "Передача файлов без шифрования. Пароли идут открытым текстом.", SurveillanceLevel.Danger),
        [22] = new PortInfo("SSH-сервер", "Удалённое управление компьютером через командную строку.", SurveillanceLevel.Recommended),
        [23] = new PortInfo("Telnet", "Удалённое управление без шифрования. Сегодня им пользуются почти только взломщики.", SurveillanceLevel.Danger),
        [80] = new PortInfo("Веб-сервер", "На компьютере работает сайт или веб-панель какой-то программы.", SurveillanceLevel.Recommended),
        [443] = new PortInfo("Веб-сервер (HTTPS)", "На компьютере работает сайт или веб-панель какой-то программы.", SurveillanceLevel.Recommended),
        [135] = new PortInfo("Службы Windows (RPC)", "Нужен самой Windows. Брандмауэр не пускает к нему из интернета.", SurveillanceLevel.DoNotTouch),
        [139] = new PortInfo("Общий доступ к файлам (NetBIOS)", "Через него другие компьютеры сети видят ваши общие папки. Дома без общих папок не нужен.", SurveillanceLevel.Recommended),
        [445] = new PortInfo("Общий доступ к файлам (SMB)", "Через него распространялись WannaCry и другие черви. Если не делитесь папками по сети, закройте.", SurveillanceLevel.Recommended),
        [1433] = new PortInfo("SQL Server", "База данных, доступная из сети.", SurveillanceLevel.Recommended),
        [3306] = new PortInfo("MySQL", "База данных, доступная из сети.", SurveillanceLevel.Recommended),
        [3389] = new PortInfo("Удалённый рабочий стол (RDP)", "Позволяет управлять компьютером издалека. Главная цель подбора паролей и взломов.", SurveillanceLevel.Danger),
        [5900] = new PortInfo("VNC (удалённый экран)", "Кто-то может видеть экран и управлять мышью.", SurveillanceLevel.Danger),
        [5985] = new PortInfo("Удалённое управление Windows (WinRM)", "Удалённый запуск команд. Домашнему компьютеру не нужен.", SurveillanceLevel.Recommended),
        [5986] = new PortInfo("Удалённое управление Windows (WinRM)", "Удалённый запуск команд. Домашнему компьютеру не нужен.", SurveillanceLevel.Recommended),
        [5040] = new PortInfo("Служба платформы устройств Windows", "Связь с телефоном и другими устройствами.", SurveillanceLevel.Harmless),
        [5357] = new PortInfo("Обнаружение устройств в сети", "Нужно, чтобы видеть принтеры и компьютеры в домашней сети.", SurveillanceLevel.Harmless),
        [7680] = new PortInfo("Оптимизация доставки обновлений", "Windows раздаёт скачанные обновления другим компьютерам.", SurveillanceLevel.Harmless)
    };

    public static PortInfo Describe(int port, bool systemProcess)
    {
        if (Known.TryGetValue(port, out PortInfo? info))
        {
            return info;
        }

        if (port >= 49152 && systemProcess)
        {
            return new PortInfo("Служебный порт Windows", "Временный порт системных служб. Брандмауэр не пускает к нему из интернета.", SurveillanceLevel.DoNotTouch);
        }

        return new PortInfo("Порт " + port, "Программа принимает подключения из сети. Если вы не знаете, зачем, закройте.", SurveillanceLevel.Unknown);
    }
}

public sealed record KnownService(string Name, string Title, string Explanation, SurveillanceLevel Level);

public sealed record KnownTask(string Path, string Title, string Explanation, SurveillanceLevel Level);

public sealed record KnownBackgroundProgram(string Marker, string Title, string Explanation, SurveillanceLevel Level);

public static class TelemetryCatalog
{
    public static IReadOnlyList<KnownService> Services { get; } = new[]
    {
        new KnownService("DiagTrack", "Сбор диагностики Windows", "Главная служба телеметрии: собирает данные об использовании компьютера и отправляет в Microsoft.", SurveillanceLevel.Recommended),
        new KnownService("dmwappushservice", "Маршрутизация push-сообщений WAP", "Помощник телеметрии: передаёт собранные данные.", SurveillanceLevel.Recommended),
        new KnownService("RetailDemo", "Демонстрационный режим магазина", "Нужен только на витринных компьютерах.", SurveillanceLevel.Recommended),
        new KnownService("wisvc", "Программа предварительной оценки Windows", "Отправляет расширенную диагностику, если вы участвуете в тестировании Windows.", SurveillanceLevel.Recommended),
        new KnownService("NvTelemetryContainer", "Телеметрия NVIDIA", "Отправляет в NVIDIA данные об играх и использовании видеокарты.", SurveillanceLevel.Recommended),
        new KnownService("lfsvc", "Служба геолокации", "Определяет, где находится компьютер, для приложений и сайтов. Без неё не работают карты и погода «рядом».", SurveillanceLevel.Unknown),
        new KnownService("PcaSvc", "Помощник по совместимости программ", "Следит за запуском программ и сообщает о проблемах совместимости.", SurveillanceLevel.Unknown),
        new KnownService("WerSvc", "Отчёты об ошибках Windows", "Отправляет в Microsoft отчёты о сбоях программ, иногда с кусками их памяти.", SurveillanceLevel.Unknown),
        new KnownService("gupdate", "Обновление Google", "Обновляет Chrome и сообщает Google версию и идентификатор установки. Отключать не советую: это обновления безопасности браузера.", SurveillanceLevel.Harmless),
        new KnownService("edgeupdate", "Обновление Microsoft Edge", "Обновляет Edge. Отключать не советую: это обновления безопасности браузера.", SurveillanceLevel.Harmless),
        new KnownService("AdobeARMservice", "Обновление Adobe Acrobat", "Обновляет Acrobat Reader и сообщает Adobe версию.", SurveillanceLevel.Harmless),
        new KnownService("wuauserv", "Центр обновления Windows", "Скачивает обновления безопасности. Без него компьютер остаётся с известными дырами.", SurveillanceLevel.DoNotTouch),
        new KnownService("WinDefend", "Защитник Windows", "Антивирус Windows. Отправляет образцы подозрительных файлов в Microsoft, но это и есть его защита.", SurveillanceLevel.DoNotTouch)
    };

    public static IReadOnlyList<KnownTask> Tasks { get; } = new[]
    {
        new KnownTask(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", "Оценка совместимости", "Собирает список установленных программ и устройств и отправляет в Microsoft.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Windows\Application Experience\ProgramDataUpdater", "Сбор данных о программах", "Собирает сведения об использовании программ.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", "Программа улучшения качества", "Отправляет в Microsoft статистику использования Windows.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", "Статистика USB-устройств", "Отправляет в Microsoft сведения о подключённых USB-устройствах.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Windows\Autochk\Proxy", "Сбор данных автопроверки диска", "Отправляет телеметрию проверки диска.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", "Сбор данных о дисках", "Отправляет в Microsoft сведения о дисках.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Windows\Feedback\Siuf\DmClient", "Отзывы о Windows", "Собирает данные для запросов отзывов.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload", "Отзывы о Windows (сценарии)", "Собирает данные для запросов отзывов.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Windows\Windows Error Reporting\QueueReporting", "Отправка отчётов об ошибках", "Досылает в Microsoft накопленные отчёты о сбоях.", SurveillanceLevel.Unknown),
        new KnownTask(@"\Microsoft\Office\OfficeTelemetryAgentLogOn2016", "Телеметрия Office", "Собирает данные об использовании Office.", SurveillanceLevel.Recommended),
        new KnownTask(@"\Microsoft\Office\OfficeTelemetryAgentFallBack2016", "Телеметрия Office (резерв)", "Собирает данные об использовании Office.", SurveillanceLevel.Recommended)
    };

    public static IReadOnlyList<KnownBackgroundProgram> BackgroundPrograms { get; } = new[]
    {
        new KnownBackgroundProgram("onedrive", "OneDrive", "Постоянно отправляет ваши файлы из Документов и Рабочего стола в облако Microsoft.", SurveillanceLevel.Recommended),
        new KnownBackgroundProgram("yandexdisk", "Яндекс Диск", "Постоянно отправляет ваши файлы в облако Яндекса.", SurveillanceLevel.Recommended),
        new KnownBackgroundProgram("ccxprocess", "Adobe Creative Cloud Experience", "Фоновый процесс Adobe, отправляет статистику использования.", SurveillanceLevel.Recommended),
        new KnownBackgroundProgram("adobegcinvoker", "Проверка лицензии Adobe", "Регулярно связывается с Adobe.", SurveillanceLevel.Unknown),
        new KnownBackgroundProgram("browser assistant", "Помощник браузера Opera", "Висит в фоне и показывает рекламные уведомления.", SurveillanceLevel.Recommended),
        new KnownBackgroundProgram("--no-startup-window", "Фоновый запуск браузера", "Браузер запускается вместе с Windows и работает в фоне, даже когда закрыт.", SurveillanceLevel.Recommended),
        new KnownBackgroundProgram("yandex", "Программа Яндекса", "Запускается вместе с Windows и работает в фоне.", SurveillanceLevel.Recommended),
        new KnownBackgroundProgram("mail.ru", "Программа Mail.ru", "Запускается вместе с Windows и работает в фоне.", SurveillanceLevel.Recommended),
        new KnownBackgroundProgram("cortana", "Кортана", "Голосовой помощник Microsoft.", SurveillanceLevel.Recommended),
        new KnownBackgroundProgram("teams", "Microsoft Teams", "Запускается вместе с Windows и держит связь с Microsoft.", SurveillanceLevel.Unknown),
        new KnownBackgroundProgram("discord", "Discord", "Запускается вместе с Windows. Видит, во что вы играете, и показывает это друзьям.", SurveillanceLevel.Unknown),
        new KnownBackgroundProgram("steam", "Steam", "Запускается вместе с Windows.", SurveillanceLevel.Harmless),
        new KnownBackgroundProgram("epicgameslauncher", "Epic Games Launcher", "Запускается вместе с Windows.", SurveillanceLevel.Harmless),
        new KnownBackgroundProgram("spotify", "Spotify", "Запускается вместе с Windows.", SurveillanceLevel.Harmless),
        new KnownBackgroundProgram("securityhealth", "Значок безопасности Windows", "Показывает состояние Защитника в трее.", SurveillanceLevel.DoNotTouch)
    };

    public static KnownBackgroundProgram? MatchBackgroundProgram(string name, string command)
    {
        string text = (name + " " + command).ToLowerInvariant();
        return BackgroundPrograms.FirstOrDefault(program => text.Contains(program.Marker));
    }

    public static bool? ReadTaskEnabled(string taskXml)
    {
        try
        {
            XDocument document = XDocument.Parse(taskXml);
            XNamespace taskNamespace = document.Root?.Name.Namespace ?? XNamespace.None;
            XElement? enabled = document.Root?.Element(taskNamespace + "Settings")?.Element(taskNamespace + "Enabled");
            return enabled == null || !string.Equals(enabled.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}

public static class CapabilityAccess
{
    public static string DecodeProgramPath(string registryKeyName)
    {
        return registryKeyName.Replace('#', '\\');
    }

    public static string DescribePackage(string packageKeyName)
    {
        int underscore = packageKeyName.IndexOf('_');
        string name = underscore > 0 ? packageKeyName.Substring(0, underscore) : packageKeyName;
        int dot = name.IndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
    }

    public static DateTime? FromFileTime(object? value)
    {
        if (value is not long ticks || ticks <= 0)
        {
            return null;
        }

        try
        {
            return DateTime.FromFileTime(ticks);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static SurveillanceLevel Assess(DateTime? lastStart, DateTime? lastStop, DateTime now, out string usage)
    {
        if (lastStart != null && lastStop == null)
        {
            usage = "использует прямо сейчас";
            return SurveillanceLevel.Danger;
        }

        if (lastStop == null)
        {
            usage = "доступ есть, но ещё не пользовалась";
            return SurveillanceLevel.Harmless;
        }

        TimeSpan ago = now - lastStop.Value;
        usage = "последний раз: " + lastStop.Value.ToString("dd.MM.yyyy HH:mm");
        return ago < TimeSpan.FromDays(7) ? SurveillanceLevel.Recommended : SurveillanceLevel.Harmless;
    }
}

public static class StartupApproval
{
    public static bool IsDisabled(byte[]? value)
    {
        return value != null && value.Length > 0 && (value[0] & 1) == 1;
    }

    public static byte[] CreateValue(bool disabled)
    {
        byte[] value = new byte[12];
        value[0] = disabled ? (byte)3 : (byte)2;
        return value;
    }
}
