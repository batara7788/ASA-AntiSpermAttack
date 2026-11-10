using Asa.App.Services;
using Asa.Core;

namespace Asa.App.Interface.Pages;

public sealed class ScanPage : PageBase
{
    private readonly Button quickButton = Theme.CreateButton("Быстрая проверка", true);
    private readonly Button fullButton = Theme.CreateButton("Полная проверка");
    private readonly Button folderButton = Theme.CreateButton("Выбрать папку");
    private readonly Button fileButton = Theme.CreateButton("Выбрать файл");
    private readonly Button processesButton = Theme.CreateButton("Запущенные процессы");
    private readonly Button cancelButton = Theme.CreateButton("Остановить");
    private readonly Label statusLabel = Theme.CreateLabel("Проверка не запущена");
    private readonly Label currentFileLabel = Theme.CreateLabel(string.Empty, Theme.SmallFont, Theme.MutedText);
    private readonly ProgressBar progressBar = new() { Style = ProgressBarStyle.Marquee, Height = 6, Visible = false, MarqueeAnimationSpeed = 25 };
    private readonly ListView threatsList = Theme.CreateListView(("Угроза", 240), ("Файл", 520), ("Что сделано", 260));
    private CancellationTokenSource? scanCancellation;

    public ScanPage(ProtectionHost host)
        : base(host, "Проверка", "Ручная проверка компьютера, папки или файла", PageIcon.Telescope)
    {
        cancelButton.Enabled = false;
        quickButton.Click += (_, _) => StartScan("Быстрая проверка", ProtectionHost.GetQuickScanLocations);
        fullButton.Click += (_, _) => StartScan("Полная проверка", ProtectionHost.GetFullScanLocations);
        processesButton.Click += (_, _) => StartScan("Проверка процессов", ProcessMonitor.GetRunningExecutables);
        folderButton.Click += (_, _) => ChooseFolder();
        fileButton.Click += (_, _) => ChooseFile();
        cancelButton.Click += (_, _) => scanCancellation?.Cancel();

        FlowLayoutPanel buttons = Theme.CreateButtonRow();
        buttons.Controls.AddRange(new Control[] { quickButton, fullButton, folderButton, fileButton, processesButton, cancelButton });

        StretchToContentWidth(progressBar);
        StretchToContentWidth(currentFileLabel);
        currentFileLabel.AutoSize = false;
        currentFileLabel.Height = 22;
        currentFileLabel.AutoEllipsis = true;

        Panel threatsPanel = CreateFillPanel(340);
        threatsPanel.Controls.Add(threatsList);

        Content.Controls.Add(buttons);
        Content.Controls.Add(statusLabel);
        Content.Controls.Add(progressBar);
        Content.Controls.Add(currentFileLabel);
        Content.Controls.Add(Theme.CreateLabel("Найденные угрозы", Theme.SubtitleFont));
        Content.Controls.Add(threatsPanel);
    }

    public bool IsScanning => scanCancellation != null;

    public void StartScan(string title, Func<List<string>> getLocations)
    {
        if (scanCancellation != null)
        {
            MessageBox.Show("Уже идёт проверка. Дождитесь окончания или остановите её.", "ASA", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!Host.Scanner.EngineReady)
        {
            MessageBox.Show("Движок ещё готовится: " + Host.Engine.StatusText, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _ = RunScanAsync(title, getLocations);
    }

    private async Task RunScanAsync(string title, Func<List<string>> getLocations)
    {
        scanCancellation = new CancellationTokenSource();
        SetScanningState(true);
        threatsList.Items.Clear();
        statusLabel.Text = title + ": собираю список файлов...";
        DateTime started = DateTime.Now;
        ScanProgress lastProgress = new(0, 0, string.Empty);

        var progress = new Progress<ScanProgress>(value =>
        {
            lastProgress = value;
            statusLabel.Text = $"{title}: проверено {value.FilesScanned:N0}, угроз {value.ThreatsFound}";
            currentFileLabel.Text = value.CurrentFile;
        });

        Host.Journal.Information(title + " начата");
        try
        {
            List<string> locations = await Task.Run(getLocations);
            await Task.Run(() => Host.Scanner.ScanLocationsAsync(locations, title, progress, threat => RunOnInterface(() => AddThreat(threat)), scanCancellation.Token));
            await Task.Delay(100);
            string duration = (DateTime.Now - started).ToString(@"hh\:mm\:ss");
            statusLabel.Text = $"{title} завершена за {duration}: проверено {lastProgress.FilesScanned:N0}, угроз {lastProgress.ThreatsFound}";
            Host.Journal.Information(statusLabel.Text);
            if (lastProgress.ThreatsFound == 0)
            {
                Host.Sounds.Play(SoundEvent.ScanCompleted);
            }

            Host.Notifier.Show(title + " завершена", lastProgress.ThreatsFound == 0 ? "Угроз не найдено" : "Найдено угроз: " + lastProgress.ThreatsFound, lastProgress.ThreatsFound == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = $"{title} остановлена: проверено {lastProgress.FilesScanned:N0}";
            Host.Journal.Information(statusLabel.Text);
        }
        catch (Exception exception)
        {
            statusLabel.Text = title + " прервана ошибкой: " + exception.Message;
            Host.Journal.Warning(statusLabel.Text);
        }
        finally
        {
            currentFileLabel.Text = string.Empty;
            scanCancellation.Dispose();
            scanCancellation = null;
            SetScanningState(false);
        }
    }

    private void AddThreat(ThreatEvent threat)
    {
        var item = new ListViewItem(new[] { threat.Result.Details, threat.Result.Path, threat.ActionText })
        {
            ForeColor = threat.Quarantined ? Theme.Warning : Theme.Danger
        };
        threatsList.Items.Insert(0, item);
    }

    private void SetScanningState(bool scanning)
    {
        progressBar.Visible = scanning;
        cancelButton.Enabled = scanning;
        foreach (Button button in new[] { quickButton, fullButton, folderButton, fileButton, processesButton })
        {
            button.Enabled = !scanning;
        }
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Какую папку проверить?", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            string folder = dialog.SelectedPath;
            StartScan("Проверка папки", () => new List<string> { folder });
        }
    }

    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog { Title = "Какой файл проверить?", Multiselect = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            List<string> files = dialog.FileNames.ToList();
            StartScan("Проверка файлов", () => files);
        }
    }
}
