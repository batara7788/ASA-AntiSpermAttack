using Asa.App.Interface.Controls;
using Asa.App.Services;

namespace Asa.App.Interface.Pages;

public sealed class OverviewPage : PageBase
{
    private readonly StatusShield shield = new();
    private readonly StatCard scannedCard = new("Проверено файлов", Theme.AccentSecondary);
    private readonly StatCard threatsCard = new("Угроз поймано", Theme.Danger);
    private readonly StatCard quarantineCard = new("В карантине", Theme.Warning);
    private readonly StatCard webShieldCard = new("Сайтов заблокировано", Theme.Accent);
    private readonly ActivityChart chart = new();
    private readonly FlowLayoutPanel modulesPanel = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Color.Transparent };
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 2000 };

    public OverviewPage(ProtectionHost host, Action quickScan)
        : base(host, "Обзор", "Состояние защиты компьютера", PageIcon.Shield)
    {
        StretchToContentWidth(shield);
        StretchToContentWidth(chart);
        shield.Margin = new Padding(0, 0, 0, 16);

        var quickScanButton = Theme.CreateButton("Быстрая проверка", true);
        quickScanButton.Click += (_, _) => quickScan();

        var cards = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = Color.Transparent };
        cards.Controls.AddRange(new Control[] { scannedCard, threatsCard, quarantineCard, webShieldCard });

        Content.Controls.Add(shield);
        Content.Controls.Add(quickScanButton);
        Content.Controls.Add(cards);
        Content.Controls.Add(chart);
        Content.Controls.Add(Theme.CreateLabel("Модули защиты", Theme.SubtitleFont));
        Content.Controls.Add(modulesPanel);

        host.Engine.StateChanged += () => RunOnInterface(RefreshView);
        host.ModulesChanged += () => RunOnInterface(RefreshView);
        host.Quarantine.Changed += () => RunOnInterface(RefreshView);
        refreshTimer.Tick += (_, _) => RefreshView();
        refreshTimer.Start();
    }

    public override void OnShown()
    {
        RefreshView();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshView()
    {
        if (!Visible)
        {
            return;
        }

        UpdateShield();
        scannedCard.SetValue(Host.Statistics.TotalFilesScanned.ToString("N0"));
        threatsCard.SetValue(Host.Statistics.TotalThreatsFound.ToString("N0"));
        quarantineCard.SetValue(Host.Quarantine.GetItems().Count.ToString());
        webShieldCard.SetValue(Host.WebShield.GetBlockedDomainCount().ToString("N0"));
        chart.SetData(Host.Statistics.GetLastDays(14));
        UpdateModules();
    }

    private void UpdateShield()
    {
        switch (Host.Engine.State)
        {
            case EngineState.Ready when Host.FullyProtected:
                shield.SetStatus(ProtectionLevel.Protected, "Вы защищены", "Все системы работают. " + Host.Engine.Version);
                break;
            case EngineState.Ready:
                shield.SetStatus(ProtectionLevel.AtRisk, "Защита в реальном времени выключена", "Включите её в настройках, иначе новые файлы не проверяются.");
                break;
            case EngineState.Failed:
                shield.SetStatus(ProtectionLevel.AtRisk, "Движок не работает", Host.Engine.StatusText);
                break;
            default:
                shield.SetStatus(ProtectionLevel.Loading, "Подготовка защиты", Host.Engine.StatusText);
                break;
        }
    }

    private void UpdateModules()
    {
        var modules = new (string Name, bool Active)[]
        {
            ("Защита в реальном времени", Host.RealTime.IsRunning),
            ("Проверка флешек", Host.Usb.IsRunning),
            ("Проверка процессов", Host.Processes.IsRunning),
            ("Слежка за автозагрузкой", Host.Autoruns.IsRunning),
            ("Защита от шифровальщиков", Host.Ransomware.IsRunning),
            ("Веб-щит", Host.Settings.WebShieldEnabled && Host.WebShield.GetBlockedDomainCount() > 0),
            ("Игровой режим", Host.Settings.GameModeEnabled),
            ("Проверка по расписанию", Host.Settings.ScheduledScanEnabled)
        };

        modulesPanel.SuspendLayout();
        modulesPanel.Controls.Clear();
        foreach ((string name, bool active) in modules)
        {
            var chip = new Label
            {
                Text = (active ? "● " : "○ ") + name,
                AutoSize = true,
                Padding = new Padding(12, 7, 12, 7),
                Margin = new Padding(0, 0, 10, 10),
                BackColor = Theme.Card,
                ForeColor = active ? Theme.Success : Theme.MutedText,
                Font = Theme.BodyFont
            };
            modulesPanel.Controls.Add(chip);
        }

        modulesPanel.ResumeLayout();
    }
}
