using Asa.App.Services;

namespace Asa.App.Interface.Pages;

public sealed class WebShieldPage : PageBase
{
    private readonly Label statusLabel = Theme.CreateLabel(string.Empty, Theme.SubtitleFont);
    private readonly Label detailsLabel = Theme.CreateLabel(string.Empty, Theme.BodyFont, Theme.MutedText);
    private readonly CheckBox enabledCheckBox;
    private readonly Button updateButton = Theme.CreateButton("Обновить список сейчас", true);

    public WebShieldPage(ProtectionHost host)
        : base(host, "Веб-щит", "Блокирует сайты, которые раздают вирусы. Работает во всех браузерах и программах")
    {
        enabledCheckBox = Theme.CreateCheckBox("Веб-щит включён", host.Settings.WebShieldEnabled);
        enabledCheckBox.CheckedChanged += async (_, _) => await ToggleAsync();
        updateButton.Click += async (_, _) => await UpdateAsync();

        Content.Controls.Add(statusLabel);
        Content.Controls.Add(detailsLabel);
        Content.Controls.Add(enabledCheckBox);
        Content.Controls.Add(updateButton);
        Content.Controls.Add(Theme.CreateLabel(
            "Как это работает: ASA раз в сутки скачивает список опасных сайтов из базы URLhaus (abuse.ch) и прописывает их в системный файл hosts. " +
            "Браузер не сможет открыть такой сайт. Ваши собственные записи в hosts не трогаются, а копия исходного файла лежит рядом: hosts.asa-backup.",
            Theme.BodyFont, Theme.MutedText));
    }

    public override void OnShown()
    {
        RefreshView();
    }

    private void RefreshView()
    {
        int count = Host.WebShield.GetBlockedDomainCount();
        bool active = Host.Settings.WebShieldEnabled && count > 0;
        statusLabel.Text = active ? "● Веб-щит работает" : "○ Веб-щит не активен";
        statusLabel.ForeColor = active ? Theme.Success : Theme.MutedText;
        string lastUpdate = Host.Settings.LastWebShieldUpdate == DateTime.MinValue ? "ещё не обновлялся" : Host.Settings.LastWebShieldUpdate.ToString("dd.MM.yyyy HH:mm");
        detailsLabel.Text = $"Заблокировано сайтов: {count:N0}. Последнее обновление: {lastUpdate}";
        updateButton.Enabled = Host.Settings.WebShieldEnabled;
    }

    private async Task ToggleAsync()
    {
        Host.Settings.WebShieldEnabled = enabledCheckBox.Checked;
        Host.SaveSettings();

        if (enabledCheckBox.Checked)
        {
            await UpdateAsync();
            return;
        }

        try
        {
            await Task.Run(Host.WebShield.Disable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Не удалось изменить hosts: " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshView();
        Host.ApplyModuleSettings();
    }

    private async Task UpdateAsync()
    {
        updateButton.Enabled = false;
        detailsLabel.Text = "Скачиваю список опасных сайтов...";
        try
        {
            await Task.Run(() => Host.WebShield.UpdateAsync(CancellationToken.None));
            Host.Settings.LastWebShieldUpdate = DateTime.Now;
            Host.SaveSettings();
        }
        catch (Exception exception)
        {
            MessageBox.Show("Не удалось обновить веб-щит: " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshView();
        Host.ApplyModuleSettings();
    }
}
