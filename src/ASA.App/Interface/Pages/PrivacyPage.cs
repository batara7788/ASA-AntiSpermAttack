using Asa.App.Services;
using Asa.Core;

namespace Asa.App.Interface.Pages;

public sealed class PrivacyPage : PageBase
{
    private readonly PrivacyService privacy;
    private readonly Label scoreLabel = Theme.CreateLabel(string.Empty, Theme.TitleFont);
    private readonly Label scoreDetailsLabel = Theme.CreateLabel(string.Empty, Theme.BodyFont, Theme.MutedText);
    private readonly Label trackerStatusLabel = Theme.CreateLabel(string.Empty);
    private readonly CheckBox trackerCheckBox;
    private readonly Button trackerUpdateButton = Theme.CreateButton("Обновить список сейчас");
    private readonly Label dnsStatusLabel = Theme.CreateLabel(string.Empty);
    private readonly ComboBox dnsProviderComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label dnsProviderDescription = Theme.CreateLabel(string.Empty, Theme.SmallFont, Theme.MutedText);
    private readonly Button dnsEnableButton = Theme.CreateButton("Включить шифрованный DNS", true);
    private readonly Button dnsDisableButton = Theme.CreateButton("Выключить");
    private readonly Dictionary<PrivacySetting, CheckBox> settingCheckBoxes = new();
    private bool refreshing;

    public PrivacyPage(ProtectionHost host)
        : base(host, "Приватность", "Закрывает слежку со стороны сайтов, рекламных сетей, Windows и провайдера", PageIcon.Eye)
    {
        privacy = host.Privacy;

        var applyAllButton = Theme.CreateButton("Закрыть всё рекомендуемое", true);
        var revertAllButton = Theme.CreateButton("Вернуть всё как было");
        applyAllButton.Click += async (_, _) => await ApplyRecommendedAsync(applyAllButton);
        revertAllButton.Click += async (_, _) => await RevertEverythingAsync(revertAllButton);
        FlowLayoutPanel scoreButtons = Theme.CreateButtonRow();
        scoreButtons.Controls.AddRange(new Control[] { applyAllButton, revertAllButton });
        AddSection(scoreLabel, scoreDetailsLabel, scoreButtons);

        trackerCheckBox = Theme.CreateCheckBox("Блокировать рекламные и следящие адреса во всех программах", false);
        trackerCheckBox.CheckedChanged += async (_, _) => await ToggleTrackersAsync();
        trackerUpdateButton.Click += async (_, _) => await UpdateTrackersAsync();
        AddSection(
            Theme.CreateLabel("Трекеры и рекламные сети", Theme.SubtitleFont),
            Theme.CreateLabel("Сайты встраивают чужие счётчики (Google Analytics, DoubleClick, Meta и тысячи других), которые собирают ваши действия в один профиль. ASA запрещает компьютеру обращаться к этим адресам. Работает во всех браузерах и программах, список обновляется раз в неделю.", Theme.BodyFont, Theme.MutedText),
            trackerCheckBox,
            trackerStatusLabel,
            trackerUpdateButton);

        foreach (DnsProvider provider in EncryptedDnsPlan.Providers)
        {
            dnsProviderComboBox.Items.Add(provider.Name);
        }

        int savedProvider = EncryptedDnsPlan.Providers.ToList().FindIndex(provider => provider.Id == host.Settings.DnsProviderId);
        dnsProviderComboBox.SelectedIndex = Math.Max(0, savedProvider);
        dnsProviderComboBox.SelectedIndexChanged += (_, _) => UpdateDnsDescription();
        dnsEnableButton.Click += async (_, _) => await EnableDnsAsync();
        dnsDisableButton.Click += async (_, _) => await DisableDnsAsync();
        FlowLayoutPanel dnsRow = Theme.CreateButtonRow();
        dnsRow.Controls.AddRange(new Control[] { new GoldFrame(dnsProviderComboBox, 240), dnsEnableButton, dnsDisableButton });
        AddSection(
            Theme.CreateLabel("Шифрованный DNS: провайдер не видит, какие сайты вы открываете", Theme.SubtitleFont),
            Theme.CreateLabel("Обычно компьютер открытым текстом спрашивает у провайдера адрес каждого сайта, и провайдер видит весь список. С шифрованным DNS эти вопросы уходят в зашифрованном виде независимой службе.", Theme.BodyFont, Theme.MutedText),
            dnsStatusLabel,
            dnsRow,
            dnsProviderDescription);

        foreach (IGrouping<string, PrivacySetting> group in privacy.Settings.GroupBy(setting => setting.Group))
        {
            var controls = new List<Control> { Theme.CreateLabel(group.Key, Theme.SubtitleFont) };
            if (group.Key != PrivacyCatalog.WindowsGroup)
            {
                controls.Add(Theme.CreateLabel("После включения браузер напишет «Управляется вашей организацией». Это нормально: так он показывает, что настройки закрепил ASA. Нужен перезапуск браузера.", Theme.SmallFont, Theme.MutedText));
            }

            foreach (PrivacySetting setting in group)
            {
                CheckBox checkBox = Theme.CreateCheckBox(setting.Title + (setting.Recommended ? string.Empty : " (по желанию)"), false);
                checkBox.Margin = new Padding(0, 6, 0, 0);
                PrivacySetting captured = setting;
                checkBox.CheckedChanged += (_, _) => ToggleSetting(captured, checkBox);
                settingCheckBoxes[setting] = checkBox;
                controls.Add(checkBox);
                Label description = Theme.CreateLabel(setting.Description, Theme.SmallFont, Theme.MutedText);
                description.Margin = new Padding(28, 0, 0, 4);
                controls.Add(description);
            }

            AddSection(controls.ToArray());
        }

        UpdateDnsDescription();
    }

    public override void OnShown()
    {
        RefreshView();
    }

    private void RefreshView()
    {
        refreshing = true;
        try
        {
            int applied = 0;
            foreach ((PrivacySetting setting, CheckBox checkBox) in settingCheckBoxes)
            {
                bool isApplied = privacy.Manager.IsApplied(setting);
                checkBox.Checked = isApplied;
                if (isApplied)
                {
                    applied++;
                }
            }

            int trackerCount = privacy.Trackers.GetBlockedDomainCount();
            trackerCheckBox.Checked = trackerCount > 0;
            trackerUpdateButton.Enabled = trackerCount > 0;
            string lastUpdate = Host.Settings.LastTrackerUpdate == DateTime.MinValue ? "ещё не обновлялся" : Host.Settings.LastTrackerUpdate.ToString("dd.MM.yyyy HH:mm");
            trackerStatusLabel.Text = trackerCount > 0 ? $"Заблокировано адресов: {trackerCount:N0}. Список обновлён: {lastUpdate}" : "Блокировка выключена";
            trackerStatusLabel.ForeColor = trackerCount > 0 ? Theme.Success : Theme.MutedText;

            DnsStatus dns = privacy.Dns.GetStatus();
            if (!dns.Supported)
            {
                dnsStatusLabel.Text = "На этой версии Windows системного шифрованного DNS нет (он появился в Windows 11). Включите «Безопасный DNS» в настройках браузера.";
                dnsStatusLabel.ForeColor = Theme.Warning;
            }
            else if (dns.ActiveProvider != null)
            {
                dnsStatusLabel.Text = $"Включён: {dns.ActiveProvider.Name}. Защищено подключений: {dns.ProtectedAdapters} из {dns.TotalAdapters}";
                dnsStatusLabel.ForeColor = Theme.Success;
            }
            else
            {
                dnsStatusLabel.Text = "Выключен: провайдер видит адреса всех сайтов, которые вы открываете";
                dnsStatusLabel.ForeColor = Theme.MutedText;
            }

            dnsEnableButton.Enabled = dns.Supported;
            dnsDisableButton.Enabled = dns.Supported && dns.ActiveProvider != null;

            int score = PrivacyManager.CalculateScore(applied, settingCheckBoxes.Count, trackerCount > 0, dns.ActiveProvider != null);
            scoreLabel.Text = $"Приватность: {score} из 100";
            scoreLabel.ForeColor = score >= 80 ? Theme.Success : score >= 40 ? Theme.Warning : Theme.Danger;
            scoreDetailsLabel.Text =
                $"Закрыто настроек слежки: {applied} из {settingCheckBoxes.Count}. " +
                $"Трекеры: {(trackerCount > 0 ? "блокируются" : "не блокируются")}. " +
                $"DNS: {(dns.ActiveProvider != null ? "зашифрован" : "виден провайдеру")}.";
        }
        finally
        {
            refreshing = false;
        }
    }

    private void ToggleSetting(PrivacySetting setting, CheckBox checkBox)
    {
        if (refreshing)
        {
            return;
        }

        try
        {
            if (checkBox.Checked)
            {
                privacy.Manager.Apply(setting);
                Host.Journal.Information("Приватность, включено: " + setting.Title);
            }
            else
            {
                privacy.Manager.Revert(setting);
                Host.Journal.Information("Приватность, возвращено как было: " + setting.Title);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            MessageBox.Show("Не удалось изменить настройку: " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshView();
    }

    private async Task ToggleTrackersAsync()
    {
        if (refreshing)
        {
            return;
        }

        if (trackerCheckBox.Checked)
        {
            await UpdateTrackersAsync();
            return;
        }

        try
        {
            await Task.Run(privacy.Trackers.Disable);
            Host.Settings.TrackerBlockEnabled = false;
            Host.SaveSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Не удалось изменить hosts: " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshView();
    }

    private async Task<bool> UpdateTrackersAsync()
    {
        trackerStatusLabel.ForeColor = Theme.MutedText;
        trackerStatusLabel.Text = "Скачиваю список трекеров...";
        bool success = false;
        try
        {
            await Task.Run(() => privacy.Trackers.UpdateAsync(CancellationToken.None));
            Host.Settings.TrackerBlockEnabled = true;
            Host.Settings.LastTrackerUpdate = DateTime.Now;
            Host.SaveSettings();
            success = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show("Не удалось включить блокировку трекеров: " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshView();
        return success;
    }

    private DnsProvider SelectedProvider => EncryptedDnsPlan.Providers[Math.Max(0, dnsProviderComboBox.SelectedIndex)];

    private void UpdateDnsDescription()
    {
        dnsProviderDescription.Text = SelectedProvider.Description + ". Адреса сайтов увидит эта служба вместо провайдера, поэтому выбирайте ту, которой доверяете больше.";
    }

    private async Task<bool> EnableDnsAsync()
    {
        DnsProvider provider = SelectedProvider;
        dnsEnableButton.Enabled = false;
        bool success = false;
        try
        {
            await Task.Run(() => privacy.Dns.Enable(provider));
            Host.Settings.DnsProviderId = provider.Id;
            Host.SaveSettings();
            success = true;
        }
        catch (Exception exception)
        {
            Host.Journal.Warning("Шифрованный DNS не включился: " + exception.Message);
            MessageBox.Show("Не удалось включить шифрованный DNS: " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshView();
        return success;
    }

    private async Task DisableDnsAsync()
    {
        try
        {
            await Task.Run(privacy.Dns.Disable);
        }
        catch (Exception exception)
        {
            MessageBox.Show("Не удалось вернуть прежний DNS: " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshView();
    }

    private async Task ApplyRecommendedAsync(Button button)
    {
        button.Enabled = false;
        int failed = 0;
        foreach (PrivacySetting setting in privacy.Settings.Where(setting => setting.Recommended && !privacy.Manager.IsApplied(setting)))
        {
            try
            {
                privacy.Manager.Apply(setting);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                failed++;
            }
        }

        if (privacy.Trackers.GetBlockedDomainCount() == 0)
        {
            await UpdateTrackersAsync();
        }

        DnsStatus dns = privacy.Dns.GetStatus();
        if (dns.Supported && dns.ActiveProvider == null)
        {
            await EnableDnsAsync();
        }

        Host.Journal.Information("Приватность: применены все рекомендуемые настройки" + (failed > 0 ? ", не удалось: " + failed : string.Empty));
        button.Enabled = true;
        RefreshView();
        MessageBox.Show("Готово. Перезапустите браузеры, а службу диагностики Windows отключит перезагрузка компьютера.", "ASA", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task RevertEverythingAsync(Button button)
    {
        if (MessageBox.Show("Вернуть все настройки приватности, блокировку трекеров и DNS в состояние до ASA?", "ASA", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        button.Enabled = false;
        foreach (PrivacySetting setting in privacy.Settings.Where(privacy.Manager.IsApplied))
        {
            try
            {
                privacy.Manager.Revert(setting);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
            }
        }

        try
        {
            if (privacy.Trackers.GetBlockedDomainCount() > 0)
            {
                await Task.Run(privacy.Trackers.Disable);
                Host.Settings.TrackerBlockEnabled = false;
                Host.SaveSettings();
            }

            if (privacy.Dns.GetStatus().ActiveProvider != null)
            {
                await Task.Run(privacy.Dns.Disable);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show("Не всё удалось вернуть: " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Host.Journal.Information("Приватность: все изменения ASA отменены");
        button.Enabled = true;
        RefreshView();
    }
}
