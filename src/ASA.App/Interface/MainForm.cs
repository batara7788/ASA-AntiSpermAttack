using Asa.App.Interface.Pages;
using Asa.App.Services;

namespace Asa.App.Interface;

public sealed class MainForm : Form
{
    private readonly ProtectionHost host;
    private readonly bool startInBackground;
    private readonly NotifyIcon trayIcon;
    private readonly Panel pageContainer = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly FlowLayoutPanel navigation;
    private readonly Dictionary<NavigationItem, PageBase> pagesByButton = new();
    private readonly List<string> pendingCommands = new();
    private readonly OverviewPage overviewPage;
    private readonly ScanPage scanPage;
    private readonly SmartScanPage smartScanPage;
    private readonly VirusTotalPage virusTotalPage;
    private NavigationItem? activeButton;
    private bool exitRequested;

    public MainForm(ProtectionHost host, bool startInBackground)
    {
        this.host = host;
        this.startInBackground = startInBackground;

        Text = "ASA: AntiSpermAttack";
        Icon = Theme.LoadApplicationIcon();
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.BodyFont;
        MinimumSize = new Size(1100, 800);
        Size = new Size(1240, 840);
        StartPosition = FormStartPosition.CenterScreen;

        scanPage = new ScanPage(host);
        smartScanPage = new SmartScanPage(host);
        overviewPage = new OverviewPage(host, StartSmartScan);
        virusTotalPage = new VirusTotalPage(host);

        navigation = new RoyalSidebar
        {
            Dock = DockStyle.Left,
            Width = 252,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16, 14, 22, 14)
        };
        navigation.Controls.Add(CreateLogo());

        AddPage("Обзор", overviewPage);
        AddPage("Умная проверка", smartScanPage);
        AddPage("Проверка", scanPage);
        AddPage("Карантин", new QuarantinePage(host));
        AddPage("Автозагрузка", new AutorunPage(host));
        AddPage("Веб-щит", new WebShieldPage(host));
        AddPage("Приватность", new PrivacyPage(host));
        AddPage("Очистка", new CleanerPage(host));
        AddPage("VirusTotal", virusTotalPage);
        AddPage("Журнал", new JournalPage(host));
        AddPage("Настройки", new SettingsPage(host));

        Controls.Add(pageContainer);
        Controls.Add(navigation);

        trayIcon = CreateTrayIcon();
        host.Notifier.Attach(trayIcon);
        host.Ransomware.AttackSuspected += (suspect, reason) => RunOnInterface(() => HandleRansomware(suspect, reason));
        host.ScheduledScanRequested += _ => RunOnInterface(() => smartScanPage.Start(deep: true, filesToo: true));
        host.Engine.StateChanged += () => RunOnInterface(UpdateTrayText);

        ShowPage(pagesByButton.First().Key);
    }

    public void HandleExternalCommand(string command)
    {
        if (!IsHandleCreated)
        {
            lock (pendingCommands)
            {
                pendingCommands.Add(command);
            }

            return;
        }

        RunOnInterface(() => ExecuteCommand(command));
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated && startInBackground)
        {
            CreateHandle();
            value = false;
        }

        base.SetVisibleCore(value);
    }

    protected override async void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);

        List<string> commands;
        lock (pendingCommands)
        {
            commands = pendingCommands.ToList();
            pendingCommands.Clear();
        }

        foreach (string command in commands)
        {
            BeginInvoke(() => ExecuteCommand(command));
        }

        EnsureSystemIntegration();
        await host.StartAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (!exitRequested && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            host.Notifier.Show("ASA работает в фоне", "Защита продолжает работать. Иконка — рядом с часами.");
            return;
        }

        trayIcon.Visible = false;
        base.OnFormClosing(eventArgs);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ExecuteCommand(string command)
    {
        if (command.StartsWith("SCAN\t", StringComparison.Ordinal))
        {
            string path = command.Substring("SCAN\t".Length);
            ShowWindow();
            ShowPage(pagesByButton.First(pair => pair.Value == scanPage).Key);
            scanPage.StartScan("Проверка: " + Path.GetFileName(path.TrimEnd('\\')), () => new List<string> { path });
        }
        else if (command == "SHOW")
        {
            ShowWindow();
        }
    }

    private void EnsureSystemIntegration()
    {
        try
        {
            if (!SystemIntegration.IsAutostartInstalled())
            {
                SystemIntegration.InstallAutostart();
            }

            if (!SystemIntegration.IsContextMenuInstalled())
            {
                SystemIntegration.InstallContextMenu();
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            host.Journal.Warning("Не удалось настроить автозапуск или контекстное меню: " + exception.Message);
        }
    }

    private Control CreateLogo()
    {
        return new RoyalLogo { Margin = new Padding(0, 0, 0, 14) };
    }

    private static readonly string[] RomanNumerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };

    private void AddPage(string title, PageBase page)
    {
        var button = new NavigationItem(RomanNumerals[pagesByButton.Count], title) { Margin = new Padding(0, 0, 0, 1) };
        button.Click += (_, _) => ShowPage(button);
        navigation.Controls.Add(button);
        pagesByButton[button] = page;
    }

    private void ShowPage(NavigationItem button)
    {
        if (activeButton != null)
        {
            activeButton.Selected = false;
        }

        activeButton = button;
        button.Selected = true;

        PageBase page = pagesByButton[button];
        pageContainer.SuspendLayout();
        pageContainer.Controls.Clear();
        pageContainer.Controls.Add(page);
        pageContainer.ResumeLayout();
        page.OnShown();
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть ASA", null, (_, _) => ShowWindow());
        menu.Items.Add("Умная проверка", null, (_, _) => StartSmartScan());
        menu.Items.Add("Быстрая проверка", null, (_, _) => StartQuickScan());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выйти (защита отключится)", null, (_, _) => ExitApplication());

        var icon = new NotifyIcon
        {
            Icon = Theme.LoadApplicationIcon(),
            Text = "ASA: AntiSpermAttack",
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => ShowWindow();
        icon.BalloonTipClicked += (_, _) => ShowWindow();
        return icon;
    }

    private void UpdateTrayText()
    {
        string status = host.Engine.State == EngineState.Ready ? "защита включена" : host.Engine.StatusText;
        string text = "ASA: " + status;
        trayIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
    }

    private void StartSmartScan()
    {
        ShowWindow();
        ShowPage(pagesByButton.First(pair => pair.Value == smartScanPage).Key);
        smartScanPage.Start(deep: false, filesToo: true);
    }

    private void StartQuickScan()
    {
        ShowWindow();
        ShowPage(pagesByButton.First(pair => pair.Value == scanPage).Key);
        scanPage.StartScan("Быстрая проверка", ProtectionHost.GetQuickScanLocations);
    }

    private void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
    }

    private void ExitApplication()
    {
        DialogResult answer = MessageBox.Show("Если выйти, компьютер останется без защиты. Выйти?", "ASA", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        exitRequested = true;
        Close();
        Application.Exit();
    }

    private void HandleRansomware(RansomwareSuspect? suspect, string reason)
    {
        host.Sounds.Play(SoundEvent.ThreatDetected);
        if (suspect == null)
        {
            host.Notifier.Show("Похоже на шифровальщика!", reason + "\nВиновника найти не удалось. Сохраните важное на флешку и запустите полную проверку.", ToolTipIcon.Error);
            return;
        }

        ShowWindow();
        string message =
            "ASA заметил поведение шифровальщика и приостановил программу:\n\n" +
            $"{suspect.ProcessName}\n{suspect.ExecutablePath}\n\nПричина: {reason}\n\n" +
            "Да — уничтожить процесс и поместить файл в карантин.\n" +
            "Нет — это я сам (например, массовое переименование фото), продолжить работу программы.";

        DialogResult answer = MessageBox.Show(this, message, "ASA: атака шифровальщика", MessageBoxButtons.YesNo, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
        if (answer == DialogResult.Yes)
        {
            host.Ransomware.KillSuspect(suspect, host.Quarantine);
        }
        else
        {
            host.Ransomware.ResumeSuspect(suspect);
        }
    }

    private void RunOnInterface(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }
}
