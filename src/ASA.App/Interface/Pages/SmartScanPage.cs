using System.Diagnostics;
using Asa.App.Services;
using Asa.Core;

namespace Asa.App.Interface.Pages;

public sealed class SmartScanPage : PageBase
{
    private readonly Button smartButton = Theme.CreateButton("Умная проверка", true);
    private readonly Button deepButton = Theme.CreateButton("Глубокая проверка");
    private readonly Button auditButton = Theme.CreateButton("Только аудит системы");
    private readonly Button cancelButton = Theme.CreateButton("Остановить");
    private readonly Label stageLabel = Theme.CreateLabel("Проверка не запущена", Theme.SubtitleFont);
    private readonly Label currentLabel = Theme.CreateLabel(string.Empty, Theme.SmallFont, Theme.MutedText);
    private readonly ProgressBar progressBar = new() { Height = 8, Visible = false, Style = ProgressBarStyle.Continuous };
    private readonly Label summaryLabel = Theme.CreateLabel(string.Empty);
    private readonly ListView findingsList = Theme.CreateListView(("Опасность", 110), ("Что найдено", 420), ("Подробности", 560));
    private readonly Button fixButton = Theme.CreateButton("Исправить выбранное", true);
    private readonly Button trustButton = Theme.CreateButton("Доверять (не показывать)");
    private readonly Button openButton = Theme.CreateButton("Открыть место");
    private CancellationTokenSource? cancellation;

    public SmartScanPage(ProtectionHost host)
        : base(host, "Умная проверка", "Ищет вирусы там, где они обычно прячутся, и начинает с самого подозрительного", PageIcon.Target)
    {
        cancelButton.Enabled = false;
        smartButton.Click += (_, _) => Start(deep: false, filesToo: true);
        deepButton.Click += (_, _) => Start(deep: true, filesToo: true);
        auditButton.Click += (_, _) => Start(deep: false, filesToo: false);
        cancelButton.Click += (_, _) => cancellation?.Cancel();
        fixButton.Click += (_, _) => FixSelected();
        trustButton.Click += (_, _) => TrustSelected();
        openButton.Click += (_, _) => OpenSelected();
        findingsList.MultiSelect = true;
        findingsList.DoubleClick += (_, _) => ShowDetails();

        FlowLayoutPanel buttons = Theme.CreateButtonRow();
        buttons.Controls.AddRange(new Control[] { smartButton, deepButton, auditButton, cancelButton });

        AddSection(
            Theme.CreateLabel("Что делает каждая кнопка", Theme.SubtitleFont),
            Theme.CreateLabel(
                "Умная проверка (5–15 минут): находит программы, скрипты, ярлыки и файлы-притворщики в опасных местах, оценивает каждый по 10 признакам, проверяет движком ClamAV от самого опасного к безопасному, самые подозрительные дополнительно показывает Защитнику Windows, а потом проверяет 10 мест, где вирусы прячут автозапуск и ломают защиту.\n" +
                "Глубокая проверка: то же самое, а затем все остальные файлы на дисках.\n" +
                "Аудит системы (меньше минуты): только настройки Windows, задачи, службы, ярлыки, hosts, прокси, Защитник.",
                Theme.BodyFont, Theme.MutedText),
            buttons);

        StretchToContentWidth(progressBar);
        StretchToContentWidth(currentLabel);
        currentLabel.AutoSize = false;
        currentLabel.Height = 20;
        currentLabel.AutoEllipsis = true;

        Content.Controls.Add(stageLabel);
        Content.Controls.Add(progressBar);
        Content.Controls.Add(currentLabel);
        Content.Controls.Add(summaryLabel);

        FlowLayoutPanel actions = Theme.CreateButtonRow();
        actions.Controls.AddRange(new Control[] { fixButton, trustButton, openButton });
        Content.Controls.Add(actions);

        Panel listPanel = CreateFillPanel(380);
        listPanel.Controls.Add(findingsList);
        Content.Controls.Add(listPanel);
        Content.Controls.Add(Theme.CreateLabel("Двойной щелчок по строке — полное описание. Вирусы, которые нашли движки, уже в карантине; подозрительное ASA только показывает, решать вам.", Theme.SmallFont, Theme.MutedText));
    }

    public bool IsRunning => cancellation != null;

    public void Start(bool deep, bool filesToo)
    {
        if (cancellation != null)
        {
            return;
        }

        if (filesToo && !Host.Scanner.EngineReady)
        {
            MessageBox.Show("Движок ещё готовится: " + Host.Engine.StatusText + "\nМожно пока запустить только аудит системы.", "ASA", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _ = RunAsync(deep, filesToo);
    }

    private async Task RunAsync(bool deep, bool filesToo)
    {
        string title = !filesToo ? "Аудит системы" : deep ? "Глубокая проверка" : "Умная проверка";
        cancellation = new CancellationTokenSource();
        SetRunning(true);
        findingsList.Items.Clear();
        summaryLabel.Text = string.Empty;
        DateTime started = DateTime.Now;
        Host.Journal.Information(title + " начата");

        var scanProgress = new Progress<SmartScanProgress>(value =>
        {
            stageLabel.Text = value.Stage;
            currentLabel.Text = value.CurrentFile;
            if (value.Total > 0)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Maximum = (int)Math.Min(int.MaxValue, value.Total);
                progressBar.Value = (int)Math.Min(value.Done, value.Total);
            }
            else
            {
                progressBar.Style = ProgressBarStyle.Marquee;
            }

            summaryLabel.Text = $"Угроз: {value.Threats}. Подозрительного: {value.Suspicious}.";
        });
        var auditProgress = new Progress<string>(stage =>
        {
            stageLabel.Text = stage;
            progressBar.Style = ProgressBarStyle.Marquee;
        });

        var allFindings = new List<SecurityFinding>();
        int threatCount = 0;
        try
        {
            if (filesToo)
            {
                SmartScanResult result = await Task.Run(() => Host.SmartScanner.RunAsync(deep, title, scanProgress, cancellation.Token));
                threatCount = result.Threats.Count;
                foreach (ThreatEvent threat in result.Threats)
                {
                    AddThreatRow(threat);
                }

                allFindings.AddRange(result.Findings);
            }

            List<SecurityFinding> auditFindings = await Task.Run(() => Host.Auditor.Run(auditProgress, cancellation.Token));
            var trusted = new HashSet<string>(Host.Settings.TrustedFindings, StringComparer.OrdinalIgnoreCase);
            allFindings.AddRange(auditFindings.Where(finding => !trusted.Contains(finding.TrustKey)));

            foreach (SecurityFinding finding in allFindings.OrderByDescending(finding => finding.Severity))
            {
                AddFindingRow(finding);
            }

            int dangerous = allFindings.Count(finding => finding.Severity == FindingSeverity.Danger);
            int warnings = allFindings.Count(finding => finding.Severity == FindingSeverity.Warning);
            string duration = (DateTime.Now - started).ToString(@"hh\:mm\:ss");
            stageLabel.Text = $"{title} завершена за {duration}";
            summaryLabel.Text = threatCount == 0 && dangerous == 0 && warnings == 0
                ? "Всё чисто. Ни вирусов, ни подозрительного."
                : $"Вирусов (уже в карантине): {threatCount}. Опасного: {dangerous}. Стоит посмотреть: {warnings}.";
            summaryLabel.ForeColor = threatCount + dangerous > 0 ? Theme.Danger : warnings > 0 ? Theme.Warning : Theme.Success;
            Host.Journal.Information(title + ": " + summaryLabel.Text);

            if (threatCount + dangerous == 0)
            {
                Host.Sounds.Play(SoundEvent.ScanCompleted);
            }

            Host.Notifier.Show(title + " завершена", summaryLabel.Text, threatCount + dangerous > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        catch (OperationCanceledException)
        {
            stageLabel.Text = title + " остановлена";
        }
        catch (Exception exception)
        {
            stageLabel.Text = title + " прервана ошибкой: " + exception.Message;
            Host.Journal.Warning(stageLabel.Text);
        }
        finally
        {
            currentLabel.Text = string.Empty;
            cancellation.Dispose();
            cancellation = null;
            SetRunning(false);
        }
    }

    private void AddThreatRow(ThreatEvent threat)
    {
        var row = new ListViewItem(new[] { "ВИРУС", threat.Result.Details, threat.Result.Path + ". " + threat.ActionText })
        {
            ForeColor = Theme.Danger,
            Tag = null
        };
        findingsList.Items.Add(row);
    }

    private void AddFindingRow(SecurityFinding finding)
    {
        (string label, Color color) = finding.Severity switch
        {
            FindingSeverity.Danger => ("Опасно", Theme.Danger),
            FindingSeverity.Warning => ("Внимание", Theme.Warning),
            _ => ("Сведения", Theme.MutedText)
        };

        findingsList.Items.Add(new ListViewItem(new[] { label, finding.Category + ": " + finding.Title, finding.Details }) { ForeColor = color, Tag = finding });
    }

    private List<(ListViewItem Row, SecurityFinding Finding)> SelectedFindings()
    {
        return findingsList.SelectedItems.Cast<ListViewItem>()
            .Where(row => row.Tag is SecurityFinding)
            .Select(row => (row, (SecurityFinding)row.Tag!))
            .ToList();
    }

    private void FixSelected()
    {
        var selected = SelectedFindings().Where(pair => pair.Finding.Fix != null).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("У выбранных строк нет автоматического исправления. Подробности — двойным щелчком.", "ASA", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string list = string.Join("\n", selected.Select(pair => "• " + pair.Finding.FixDescription + ": " + pair.Finding.Title));
        if (MessageBox.Show("Сделать?\n\n" + list, "ASA: исправление", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        foreach ((ListViewItem row, SecurityFinding finding) in selected)
        {
            try
            {
                string message = finding.Fix!();
                Host.Journal.Information($"Исправлено: {finding.Title}. {message}");
                row.ForeColor = Theme.Success;
                row.SubItems[0].Text = "Исправлено";
                row.SubItems[2].Text = message;
                row.Tag = null;
            }
            catch (Exception exception)
            {
                Host.Journal.Warning($"Не удалось исправить «{finding.Title}»: {exception.Message}");
                row.SubItems[2].Text = "Не удалось: " + exception.Message;
            }
        }

        findingsList.Invalidate();
    }

    private void TrustSelected()
    {
        var selected = SelectedFindings();
        if (selected.Count == 0)
        {
            return;
        }

        foreach ((ListViewItem row, SecurityFinding finding) in selected)
        {
            if (!Host.Settings.TrustedFindings.Contains(finding.TrustKey))
            {
                Host.Settings.TrustedFindings.Add(finding.TrustKey);
            }

            if (finding.FilePath != null && finding.Category == "Подозрительный файл")
            {
                Host.Settings.ExcludedPaths.Add(finding.FilePath);
            }

            Host.Journal.Information("Помечено как доверенное: " + finding.Title);
            findingsList.Items.Remove(row);
        }

        Host.SaveSettings();
    }

    private void OpenSelected()
    {
        SecurityFinding? finding = SelectedFindings().Select(pair => pair.Finding).FirstOrDefault(item => item.FilePath != null);
        if (finding?.FilePath == null || !File.Exists(finding.FilePath))
        {
            return;
        }

        Process.Start("explorer.exe", "/select,\"" + finding.FilePath + "\"");
    }

    private void ShowDetails()
    {
        if (findingsList.SelectedItems.Count == 0)
        {
            return;
        }

        ListViewItem row = findingsList.SelectedItems[0];
        string text = row.SubItems[1].Text + "\n\n" + row.SubItems[2].Text;
        if (row.Tag is SecurityFinding finding && finding.FixDescription != null)
        {
            text += "\n\nМожно исправить: " + finding.FixDescription;
        }

        MessageBox.Show(text, "ASA: подробности", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SetRunning(bool running)
    {
        progressBar.Visible = running;
        cancelButton.Enabled = running;
        smartButton.Enabled = !running;
        deepButton.Enabled = !running;
        auditButton.Enabled = !running;
    }
}
