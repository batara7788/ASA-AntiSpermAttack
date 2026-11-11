namespace Asa.Core;

public static class PrivacyCatalog
{
    public const string WindowsGroup = "Windows";
    public const string ChromeGroup = "Google Chrome";
    public const string EdgeGroup = "Microsoft Edge";
    public const string YandexGroup = "Яндекс Браузер";
    public const string FirefoxGroup = "Firefox";

    private const string ChromePolicies = @"SOFTWARE\Policies\Google\Chrome";
    private const string EdgePolicies = @"SOFTWARE\Policies\Microsoft\Edge";
    private const string YandexPolicies = @"SOFTWARE\Policies\YandexBrowser";
    private const string FirefoxPolicies = @"SOFTWARE\Policies\Mozilla\Firefox";

    public static IReadOnlyList<PrivacySetting> All()
    {
        var settings = new List<PrivacySetting>();
        settings.AddRange(Windows());
        settings.AddRange(Chromium(ChromeGroup, "chrome", ChromePolicies, "Google"));
        settings.AddRange(Chromium(EdgeGroup, "edge", EdgePolicies, "Microsoft"));
        settings.AddRange(EdgeOnly());
        settings.AddRange(Chromium(YandexGroup, "yandex", YandexPolicies, "Яндекс"));
        settings.AddRange(Firefox());
        return settings;
    }

    private static RegistryChange User(string path, string name, object value) => new(RegistryRoot.CurrentUser, path, name, value);

    private static RegistryChange Machine(string path, string name, object value) => new(RegistryRoot.LocalMachine, path, name, value);

    private static PrivacySetting Create(string id, string group, string title, string description, bool recommended, params RegistryChange[] changes)
    {
        return new PrivacySetting { Id = id, Group = group, Title = title, Description = description, Recommended = recommended, Changes = changes };
    }

    private static IEnumerable<PrivacySetting> Windows()
    {
        yield return Create("win.telemetry", WindowsGroup, "Свести телеметрию Windows к минимуму",
            "Windows отправляет в Microsoft данные об использовании компьютера. На домашних версиях полностью выключить нельзя, остаётся обязательный минимум.", true,
            Machine(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
            Machine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0));

        yield return Create("win.diagtrack", WindowsGroup, "Отключить службу сбора диагностики",
            "Служба DiagTrack собирает и отправляет телеметрию. Подействует после перезагрузки.", true,
            Machine(@"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", 4));

        yield return Create("win.advertising", WindowsGroup, "Выключить рекламный идентификатор",
            "По этому номеру приложения узнают вас и показывают рекламу по интересам.", true,
            User(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
            Machine(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1));

        yield return Create("win.activity", WindowsGroup, "Не вести и не отправлять журнал действий",
            "Windows запоминает, какие программы и файлы вы открывали, и может отправлять это в Microsoft.", true,
            Machine(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
            Machine(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
            Machine(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0));

        yield return Create("win.typing", WindowsGroup, "Не отправлять набранный текст и рукописный ввод",
            "Windows собирает то, что вы печатаете, чтобы «улучшать распознавание».", true,
            User(@"Software\Microsoft\Input\TIPC", "Enabled", 0),
            User(@"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1),
            User(@"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1),
            User(@"Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0));

        yield return Create("win.tailored", WindowsGroup, "Выключить «персонализированные возможности»",
            "Microsoft использует диагностические данные, чтобы подбирать советы и рекламу.", true,
            User(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0));

        yield return Create("win.recall", WindowsGroup, "Запретить Recall (снимки экрана для ИИ)",
            "Функция Windows 11, которая регулярно снимает экран и хранит историю всего, что вы делали.", true,
            Machine(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1),
            User(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1));

        yield return Create("win.websearch", WindowsGroup, "Не отправлять поиск из меню «Пуск» в интернет",
            "Всё, что вы набираете в поиске Windows, уходит в Bing.", true,
            User(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
            User(@"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0));

        yield return Create("win.suggestions", WindowsGroup, "Убрать рекламу и «рекомендации» в Windows",
            "Реклама приложений в меню «Пуск», на экране блокировки и тихая установка рекомендованных приложений.", true,
            User(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0),
            User(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0),
            User(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0),
            User(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0),
            User(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", 0),
            User(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", 0));

        yield return Create("win.feedback", WindowsGroup, "Не спрашивать отзывы",
            "Windows перестанет просить оценить её работу.", true,
            User(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0));

        yield return Create("win.apptracking", WindowsGroup, "Не отслеживать запуски программ",
            "Windows считает, какие программы вы запускаете, для списка «часто используемые».", false,
            User(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackProgs", 0));

        yield return Create("win.errorreporting", WindowsGroup, "Не отправлять отчёты об ошибках",
            "При сбое программы Windows отправляет в Microsoft отчёт, иногда с кусками памяти программы.", false,
            Machine(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1));

        yield return Create("win.location", WindowsGroup, "Выключить определение местоположения",
            "Приложения и сайты не смогут узнать, где находится компьютер. Перестанут работать карты и погода «рядом со мной».", false,
            Machine(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", 1));
    }

    private static IEnumerable<PrivacySetting> Chromium(string group, string prefix, string policies, string vendor)
    {
        yield return Create(prefix + ".metrics", group, $"Не отправлять статистику использования в {vendor}",
            "Браузер отправляет отчёты о том, как вы им пользуетесь, и отчёты о сбоях.", true,
            Machine(policies, "MetricsReportingEnabled", 0));

        yield return Create(prefix + ".urls", group, $"Не отправлять адреса посещённых страниц в {vendor}",
            "Настройка «Улучшение поиска и просмотра»: браузер отправляет адреса страниц, которые вы открываете.", true,
            Machine(policies, "UrlKeyedAnonymizedDataCollectionEnabled", 0));

        yield return Create(prefix + ".cookies", group, "Блокировать сторонние куки",
            "Через сторонние куки рекламные сети следят за вами на разных сайтах. Редко из-за этого ломается вход на сайт через чужой аккаунт.", true,
            Machine(policies, "BlockThirdPartyCookies", 1));

        yield return Create(prefix + ".sandbox", group, "Выключить рекламные темы и измерение рекламы",
            "«Privacy Sandbox»: браузер сам определяет ваши интересы по истории и сообщает их сайтам.", true,
            Machine(policies, "PrivacySandboxAdTopicsEnabled", 0),
            Machine(policies, "PrivacySandboxSiteEnabledAdsEnabled", 0),
            Machine(policies, "PrivacySandboxAdMeasurementEnabled", 0),
            Machine(policies, "PrivacySandboxPromptEnabled", 0));

        yield return Create(prefix + ".suggest", group, "Не отправлять набираемое в адресной строке",
            "Каждая буква, набранная в адресной строке, уходит поисковику ради подсказок.", false,
            Machine(policies, "SearchSuggestEnabled", 0));

        yield return Create(prefix + ".dnt", group, "Просить сайты не отслеживать",
            "Браузер добавляет к запросам просьбу «Do Not Track». Сайты не обязаны её выполнять.", true,
            Machine(policies, "EnableDoNotTrack", 1));
    }

    private static IEnumerable<PrivacySetting> EdgeOnly()
    {
        yield return Create("edge.diagnostic", EdgeGroup, "Не отправлять диагностику и не персонализировать рекламу",
            "Edge отправляет данные о просмотре в Microsoft и использует историю для рекламы и новостей.", true,
            Machine(EdgePolicies, "DiagnosticData", 0),
            Machine(EdgePolicies, "PersonalizationReportingEnabled", 0));

        yield return Create("edge.shopping", EdgeGroup, "Выключить помощника по покупкам",
            "Edge отправляет адреса страниц магазинов, чтобы показывать купоны и сравнение цен.", true,
            Machine(EdgePolicies, "EdgeShoppingAssistantEnabled", 0));

        yield return Create("edge.tracking", EdgeGroup, "Строгая защита от отслеживания",
            "Блокирует большинство трекеров на всех сайтах. Изредка ломает отдельные сайты.", false,
            Machine(EdgePolicies, "TrackingPrevention", 3));
    }

    private static IEnumerable<PrivacySetting> Firefox()
    {
        yield return Create("firefox.telemetry", FirefoxGroup, "Не отправлять телеметрию в Mozilla",
            "Отчёты об использовании браузера и участие в исследованиях.", true,
            Machine(FirefoxPolicies, "DisableTelemetry", 1),
            Machine(FirefoxPolicies, "DisableFirefoxStudies", 1));

        yield return Create("firefox.tracking", FirefoxGroup, "Включить защиту от отслеживания",
            "Блокирует трекеры, сборщиков цифровых отпечатков и скрытые майнеры.", true,
            Machine(FirefoxPolicies + @"\EnableTrackingProtection", "Value", 1),
            Machine(FirefoxPolicies + @"\EnableTrackingProtection", "Cryptomining", 1),
            Machine(FirefoxPolicies + @"\EnableTrackingProtection", "Fingerprinting", 1));

        yield return Create("firefox.pocket", FirefoxGroup, "Убрать рекламные рекомендации Pocket",
            "Спонсорские материалы на странице новой вкладки.", true,
            Machine(FirefoxPolicies, "DisablePocket", 1));
    }
}

public sealed record DnsProvider(string Id, string Name, string Description, IReadOnlyList<string> Ipv4, IReadOnlyList<string> Ipv6, string Template);

public static class EncryptedDnsPlan
{
    public static IReadOnlyList<DnsProvider> Providers { get; } = new[]
    {
        new DnsProvider("quad9", "Quad9 (Швейцария)", "Некоммерческий, не хранит ваш адрес, сам блокирует вредоносные сайты", new[] { "9.9.9.9", "149.112.112.112" }, new[] { "2620:fe::fe", "2620:fe::9" }, "https://dns.quad9.net/dns-query"),
        new DnsProvider("adguard", "AdGuard DNS (Кипр)", "Дополнительно блокирует рекламу и трекеры на уровне DNS", new[] { "94.140.14.14", "94.140.15.15" }, new[] { "2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff" }, "https://dns.adguard-dns.com/dns-query"),
        new DnsProvider("cloudflare", "Cloudflare (США)", "Самый быстрый, обещает не хранить журналы дольше суток", new[] { "1.1.1.1", "1.0.0.1" }, new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" }, "https://cloudflare-dns.com/dns-query")
    };

    public static DnsProvider? FindByAddress(string address)
    {
        return Providers.FirstOrDefault(provider => provider.Ipv4.Contains(address) || provider.Ipv6.Contains(address, StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> BuildEnableCommands(DnsProvider provider, string adapterName, bool includeIpv6)
    {
        var commands = new List<string>();
        foreach (string address in provider.Ipv4.Concat(includeIpv6 ? provider.Ipv6 : Array.Empty<string>()))
        {
            commands.Add($"dns add encryption server={address} dohtemplate={provider.Template} autoupgrade=yes udpfallback=no");
            commands.Add($"dns set encryption server={address} dohtemplate={provider.Template} autoupgrade=yes udpfallback=no");
        }

        commands.Add($"interface ipv4 set dnsservers name=\"{adapterName}\" source=static address={provider.Ipv4[0]} register=primary validate=no");
        commands.Add($"interface ipv4 add dnsservers name=\"{adapterName}\" address={provider.Ipv4[1]} index=2 validate=no");
        if (includeIpv6)
        {
            commands.Add($"interface ipv6 set dnsservers name=\"{adapterName}\" source=static address={provider.Ipv6[0]} register=primary validate=no");
            commands.Add($"interface ipv6 add dnsservers name=\"{adapterName}\" address={provider.Ipv6[1]} index=2 validate=no");
        }

        return commands;
    }

    public static IReadOnlyList<string> BuildRestoreCommands(string adapterName, string protocol, IReadOnlyList<string> previousServers)
    {
        if (previousServers.Count == 0)
        {
            return new[] { $"interface {protocol} set dnsservers name=\"{adapterName}\" source=dhcp" };
        }

        var commands = new List<string>
        {
            $"interface {protocol} set dnsservers name=\"{adapterName}\" source=static address={previousServers[0]} register=primary validate=no"
        };
        for (int index = 1; index < previousServers.Count; index++)
        {
            commands.Add($"interface {protocol} add dnsservers name=\"{adapterName}\" address={previousServers[index]} index={index + 1} validate=no");
        }

        return commands;
    }
}
