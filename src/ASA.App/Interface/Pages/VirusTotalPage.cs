using System.Diagnostics;
using Asa.App.Services;
using Asa.Core;

namespace Asa.App.Interface.Pages;

public sealed class VirusTotalPage : PageBase
{
    private readonly TextBox keyTextBox = Theme.CreateTextBox();
    private readonly Label keyStatusLabel = Theme.CreateLabel(string.Empty, Theme.SmallFont, Theme.MutedText);
    private readonly Label fileLabel = Theme.CreateLabel("Файл не выбран", Theme.BodyFont, Theme.MutedText);
    private readonly Label verdictLabel = Theme.CreateLabel(string.Empty, Theme.TitleFont);
    private readonly Label detailsLabel = Theme.CreateLabel(string.Empty);
    private readonly LinkLabel reportLink = new() { AutoSize = true, LinkColor = Theme.AccentSecondary, Font = Theme.BodyFont, Visible = false };
    private readonly Button checkButton = Theme.CreateButton("Выбрать файл и проверить", true);
    private readonly Button uploadButton = Theme.CreateButton("Отправить файл на VirusTotal");
    private string? selectedFile;

    public VirusTotalPage(ProtectionHost host)
        : base(host, "VirusTotal", "Второе мнение: файл проверяют больше 70 антивирусов сразу", PageIcon.Scales)
    {
        keyTextBox.UseSystemPasswordChar = true;
        keyTextBox.PlaceholderText = "Ключ API VirusTotal";
        var saveKeyButton = Theme.CreateButton("Сохранить ключ");
        saveKeyButton.Click += (_, _) => SaveKey();
        var getKeyLink = new LinkLabel { Text = "Где взять бесплатный ключ", AutoSize = true, LinkColor = Theme.AccentSecondary, Font = Theme.SmallFont, Margin = new Padding(0, 0, 0, 16) };
        getKeyLink.LinkClicked += (_, _) => OpenUrl("https://www.virustotal.com/gui/my-apikey");

        FlowLayoutPanel keyRow = Theme.CreateButtonRow();
        keyRow.Controls.AddRange(new Control[] { new GoldFrame(keyTextBox, 420) { Height = 36 }, saveKeyButton });

        uploadButton.Visible = false;
        checkButton.Click += async (_, _) => await ChooseAndCheckAsync();
        uploadButton.Click += async (_, _) => await UploadAsync();
        reportLink.LinkClicked += (_, _) => OpenUrl((string)reportLink.Tag!);

        FlowLayoutPanel actionRow = Theme.CreateButtonRow();
        actionRow.Controls.AddRange(new Control[] { checkButton, uploadButton });

        getKeyLink.Margin = new Padding(0, 0, 0, 4);
        getKeyLink.BackColor = Color.Transparent;
        getKeyLink.ActiveLinkColor = Theme.PaleGold;
        reportLink.BackColor = Color.Transparent;
        reportLink.ActiveLinkColor = Theme.PaleGold;

        AddSection(Theme.CreateLabel("Ключ API", Theme.SubtitleFont), keyRow, keyStatusLabel, getKeyLink);
        AddSection(Theme.CreateLabel("Проверка файла", Theme.SubtitleFont), actionRow, fileLabel, verdictLabel, detailsLabel, reportLink);
        AddSection(
            Theme.CreateLabel("Что уходит в интернет", Theme.SubtitleFont),
            Theme.CreateLabel(
                "Сначала ASA отправляет только отпечаток файла (SHA-256), сам файл никуда не уходит. " +
                "Если VirusTotal этот файл ещё не видел, его можно отправить целиком, но только по вашему решению: загруженные файлы видят исследователи безопасности, не отправляйте личные документы.",
                Theme.BodyFont, Theme.MutedText));
    }

    public override void OnShown()
    {
        bool hasKey = ApiKeyProtector.Unprotect(Host.Settings.VirusTotalApiKeyProtected).Length > 0;
        keyStatusLabel.Text = hasKey ? "Ключ сохранён (зашифрован для вашей учётной записи Windows)" : "Ключ не задан";
    }

    public async Task CheckFileAsync(string path)
    {
        selectedFile = path;
        fileLabel.Text = path;
        uploadButton.Visible = false;
        reportLink.Visible = false;
        detailsLabel.Text = string.Empty;

        VirusTotalClient? client = CreateClient();
        if (client == null)
        {
            return;
        }

        verdictLabel.ForeColor = Theme.MutedText;
        verdictLabel.Text = "Проверяю...";
        try
        {
            string sha256 = await Task.Run(() => VirusTotalClient.ComputeSha256(path));
            VirusTotalReport report = await client.LookupHashAsync(sha256);
            ShowReport(report);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            verdictLabel.ForeColor = Theme.Warning;
            verdictLabel.Text = "Ошибка";
            detailsLabel.Text = exception.Message;
        }
    }

    private void ShowReport(VirusTotalReport report)
    {
        reportLink.Tag = report.Permalink;
        reportLink.Text = "Открыть полный отчёт на сайте";
        reportLink.Visible = true;

        if (!report.Known)
        {
            verdictLabel.ForeColor = Theme.MutedText;
            verdictLabel.Text = "VirusTotal этот файл ещё не видел";
            detailsLabel.Text = "Можно отправить его на проверку кнопкой выше.";
            uploadButton.Visible = true;
            reportLink.Visible = false;
            return;
        }

        int total = report.Malicious + report.Suspicious + report.Harmless + report.Undetected;
        if (report.Malicious >= 3)
        {
            verdictLabel.ForeColor = Theme.Danger;
            verdictLabel.Text = $"Опасно: {report.Malicious} из {total}";
        }
        else if (report.Malicious > 0 || report.Suspicious > 0)
        {
            verdictLabel.ForeColor = Theme.Warning;
            verdictLabel.Text = $"Подозрительно: {report.Malicious + report.Suspicious} из {total}";
        }
        else
        {
            verdictLabel.ForeColor = Theme.Success;
            verdictLabel.Text = $"Чисто: 0 из {total}";
        }

        detailsLabel.Text = report.PopularThreatName.Length > 0 ? "Чаще всего называют: " + report.PopularThreatName : string.Empty;
    }

    private async Task ChooseAndCheckAsync()
    {
        using var dialog = new OpenFileDialog { Title = "Какой файл проверить на VirusTotal?" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await CheckFileAsync(dialog.FileName);
        }
    }

    private async Task UploadAsync()
    {
        if (selectedFile == null)
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            "Файл будет отправлен на сервер VirusTotal, и его смогут скачать исследователи безопасности.\nНе отправляйте личные документы.\n\nОтправить?",
            "Отправка на VirusTotal",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        VirusTotalClient? client = CreateClient();
        if (client == null)
        {
            return;
        }

        uploadButton.Enabled = false;
        verdictLabel.Text = "Отправляю...";
        try
        {
            await client.UploadFileAsync(selectedFile);
            verdictLabel.ForeColor = Theme.MutedText;
            verdictLabel.Text = "Файл отправлен";
            detailsLabel.Text = "Анализ занимает пару минут. Потом проверьте файл ещё раз.";
            uploadButton.Visible = false;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            verdictLabel.ForeColor = Theme.Warning;
            verdictLabel.Text = "Ошибка";
            detailsLabel.Text = exception.Message;
        }
        finally
        {
            uploadButton.Enabled = true;
        }
    }

    private VirusTotalClient? CreateClient()
    {
        string key = ApiKeyProtector.Unprotect(Host.Settings.VirusTotalApiKeyProtected);
        if (key.Length == 0)
        {
            verdictLabel.ForeColor = Theme.Warning;
            verdictLabel.Text = "Сначала сохраните ключ API";
            return null;
        }

        return new VirusTotalClient(Host.HttpClient, key);
    }

    private void SaveKey()
    {
        Host.Settings.VirusTotalApiKeyProtected = ApiKeyProtector.Protect(keyTextBox.Text);
        Host.SaveSettings();
        keyTextBox.Clear();
        OnShown();
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
