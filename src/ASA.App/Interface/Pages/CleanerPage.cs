using Asa.App.Services;
using Asa.Core;

namespace Asa.App.Interface.Pages;

public sealed class CleanerPage : PageBase
{
    private readonly CheckedListBox locationsList = new()
    {
        BackColor = Theme.Card,
        ForeColor = Theme.Text,
        Font = Theme.BodyFont,
        BorderStyle = BorderStyle.None,
        CheckOnClick = true,
        Dock = DockStyle.Fill
    };

    private readonly Label summaryLabel = Theme.CreateLabel("Нажмите «Анализ», чтобы узнать, сколько места занимает мусор");
    private readonly Button analyzeButton = Theme.CreateButton("Анализ", true);
    private readonly Button cleanButton = Theme.CreateButton("Очистить выбранное");
    private JunkCleaner? cleaner;
    private List<JunkReport> reports = new();

    public CleanerPage(ProtectionHost host)
        : base(host, "Очистка", "Удаляет временные файлы и кэш браузеров. Пароли, история и вкладки не трогаются", PageIcon.Broom)
    {
        cleanButton.Enabled = false;
        analyzeButton.Click += async (_, _) => await AnalyzeAsync();
        cleanButton.Click += async (_, _) => await CleanAsync();

        FlowLayoutPanel buttons = Theme.CreateButtonRow();
        buttons.Controls.AddRange(new Control[] { analyzeButton, cleanButton });

        Panel listPanel = CreateFillPanel(360);
        listPanel.Padding = new Padding(10);
        listPanel.Controls.Add(locationsList);

        Content.Controls.Add(buttons);
        Content.Controls.Add(summaryLabel);
        Content.Controls.Add(listPanel);
        Content.Controls.Add(Theme.CreateLabel("Совет: перед очисткой кэша закройте браузеры, иначе часть файлов будет занята и пропущена.", Theme.SmallFont, Theme.MutedText));
    }

    private async Task AnalyzeAsync()
    {
        analyzeButton.Enabled = false;
        cleanButton.Enabled = false;
        summaryLabel.Text = "Считаю...";
        cleaner = ProtectionHost.CreateJunkCleaner();
        JunkCleaner currentCleaner = cleaner;
        reports = (await Task.Run(currentCleaner.Analyze)).Where(report => report.FileCount > 0).OrderByDescending(report => report.SizeBytes).ToList();

        locationsList.Items.Clear();
        foreach (JunkReport report in reports)
        {
            locationsList.Items.Add($"{report.Name} — {Theme.FormatBytes(report.SizeBytes)} ({report.FileCount:N0} файлов)", true);
        }

        long total = reports.Sum(report => report.SizeBytes);
        summaryLabel.Text = reports.Count == 0 ? "Мусора не найдено" : "Можно освободить: " + Theme.FormatBytes(total);
        analyzeButton.Enabled = true;
        cleanButton.Enabled = reports.Count > 0;
    }

    private async Task CleanAsync()
    {
        if (cleaner == null)
        {
            return;
        }

        List<string> selectedNames = locationsList.CheckedIndices.Cast<int>().Select(index => reports[index].Name).ToList();
        if (selectedNames.Count == 0)
        {
            return;
        }

        analyzeButton.Enabled = false;
        cleanButton.Enabled = false;
        summaryLabel.Text = "Чищу...";
        JunkCleaner currentCleaner = cleaner;
        CleanResult result = await Task.Run(() => currentCleaner.Clean(selectedNames));

        string message = $"Освобождено {Theme.FormatBytes(result.FreedBytes)}, удалено файлов: {result.DeletedFiles:N0}";
        if (result.SkippedFiles > 0)
        {
            message += $", пропущено занятых: {result.SkippedFiles:N0}";
        }

        Host.Journal.Information("Очистка: " + message);
        locationsList.Items.Clear();
        summaryLabel.Text = message;
        analyzeButton.Enabled = true;
    }
}
