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
    private readonly Button auditButton = Theme.CreateButton("Проверить, кто следит", true);
    private readonly Button watchButton = Theme.CreateButton("Наблюдать за соединениями 60 секунд");
    private readonly Button auditCancelButton = Theme.CreateButton("Остановить");
    private readonly Button auditDisableButton = Theme.CreateButton("Отключить выбранное", true);
    private readonly Button auditEnableButton = Theme.CreateButton("Вернуть выбранное");
    private readonly Button auditDetailsButton = Theme.CreateButton("Подробности");
    private readonly Label auditStatusLabel = Theme.CreateLabel("Проверка ещё не запускалась");
    private readonly ListView auditList = Theme.CreateListView(("Опасность", 120), ("Где", 150), ("Кто", 380), ("Что делает", 520));
    private CancellationTokenSource? auditCancellation;
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
        BuildAuditSection();

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

    private void BuildAuditSection()
    {
        auditCancelButton.Enabled = false;
        auditList.MultiSelect = true;
        auditButton.Click += async (_, _) => await RunAuditAsync(watch: false);
        watchButton.Click += async (_, _) => await RunAuditAsync(watch: true);
        auditCancelButton.Click += (_, _) => auditCancellation?.Cancel();
        auditDisableButton.Click += (_, _) => ChangeSelected(disable: true);
        auditEnableButton.Click += (_, _) => ChangeSelected(disable: false);
        auditDetailsButton.Click += (_, _) => ShowAuditDetails();
        auditList.DoubleClick += (_, _) => ShowAuditDetails();

        FlowLayoutPanel runRow = Theme.CreateButtonRow();
        runRow.Controls.AddRange(new Control[] { auditButton, watchButton, auditCancelButton });
        FlowLayoutPanel actionRow = Theme.CreateButtonRow();
        actionRow.Controls.AddRange(new Control[] { auditDisableButton, auditEnableButton, auditDetailsButton });

        AddSection(
            Theme.CreateLabel("Аудит слежки: кто на компьютере собирает и отправляет данные", Theme.SubtitleFont),
            Theme.CreateLabel("Показывает, какие программы и с кем связаны, какие порты открыты в сеть, какие службы и задачи собирают телеметрию, кто пользовался камерой и микрофоном, что работает в фоне и какие следы хранит Windows. ASA видит, кто, куда и как часто отправляет данные, но не их содержимое: оно зашифровано.", Theme.BodyFont, Theme.MutedText),
            runRow,
            auditStatusLabel,
            actionRow);

        Panel listPanel = CreateFillPanel(360);
        listPanel.Controls.Add(auditList);
        Content.Controls.Add(listPanel);
    }

    private async Task RunAuditAsync(bool watch)
    {
        if (auditCancellation != null)
        {
            return;
        }

        auditCancellation = new CancellationTokenSource();
        auditButton.Enabled = false;
        watchButton.Enabled = false;
        auditCancelButton.Enabled = true;
        var progress = new Progress<string>(text => auditStatusLabel.Text = text);
        auditStatusLabel.ForeColor = Theme.Text;

        try
        {
            CancellationToken token = auditCancellation.Token;
            List<SurveillanceItem> items;
            if (watch)
            {
                items = await Host.Surveillance.WatchConnectionsAsync(TimeSpan.FromSeconds(60), progress, token);
                for (int index = auditList.Items.Count - 1; index >= 0; index--)
                {
                    if (auditList.Items[index].Tag is SurveillanceItem old && old.Category == "Соединения")
                    {
                        auditList.Items.RemoveAt(index);
                    }
                }
            }
            else
            {
                items = await Task.Run(() => Host.Surveillance.Run(progress, token), token);
                auditList.Items.Clear();
            }

            auditList.BeginUpdate();
            int position = 0;
            foreach (SurveillanceItem item in items.OrderByDescending(item => item.Level).ThenBy(item => item.Category))
            {
                auditList.Items.Insert(watch ? position++ : auditList.Items.Count, CreateAuditRow(item));
            }

            auditList.EndUpdate();
            UpdateAuditSummary(watch ? "Наблюдение завершено. " : "Проверка завершена. ");
            Host.Journal.Information("Аудит слежки: " + auditStatusLabel.Text);
        }
        catch (OperationCanceledException)
        {
            auditStatusLabel.Text = "Остановлено";
        }
        catch (Exception exception)
        {
            auditStatusLabel.Text = "Ошибка: " + exception.Message;
            Host.Journal.Warning("Аудит слежки: " + exception.Message);
        }
        finally
        {
            auditCancellation.Dispose();
            auditCancellation = null;
            auditButton.Enabled = true;
            watchButton.Enabled = true;
            auditCancelButton.Enabled = false;
        }
    }

    private void UpdateAuditSummary(string prefix)
    {
        List<SurveillanceItem> items = auditList.Items.Cast<ListViewItem>().Select(row => row.Tag).OfType<SurveillanceItem>().ToList();
        int danger = items.Count(item => item.Level == SurveillanceLevel.Danger);
        int recommended = items.Count(item => item.Level == SurveillanceLevel.Recommended);
        int unknown = items.Count(item => item.Level == SurveillanceLevel.Unknown);
        int disabled = items.Count(item => item.Disabled);
        auditStatusLabel.Text = $"{prefix}Опасно: {danger}. Стоит закрыть: {recommended}. Неизвестно: {unknown}. Уже отключено: {disabled}.";
        auditStatusLabel.ForeColor = danger > 0 ? Theme.Danger : recommended > 0 ? Theme.Warning : Theme.Success;
    }

    private static ListViewItem CreateAuditRow(SurveillanceItem item)
    {
        (string label, Color color) = item.Disabled
            ? ("Отключено", Theme.Success)
            : item.Level switch
            {
                SurveillanceLevel.Danger => ("Опасно", Theme.Danger),
                SurveillanceLevel.Recommended => ("Стоит закрыть", Theme.Warning),
                SurveillanceLevel.Unknown => ("Неизвестно", Theme.PaleGold),
                SurveillanceLevel.DoNotTouch => ("Не трогать", Theme.MutedText),
                _ => ("Безвредно", Theme.MutedText)
            };

        return new ListViewItem(new[] { label, item.Category, item.Title, item.Details }) { ForeColor = color, Tag = item };
    }

    private void ChangeSelected(bool disable)
    {
        var selected = auditList.SelectedItems.Cast<ListViewItem>()
            .Where(row => row.Tag is SurveillanceItem item && (disable ? item.Disable : item.Enable) != null)
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(disable
                ? "Выберите строки, которые можно отключить. У строк «Не трогать» и у системных процессов такой возможности нет."
                : "Выберите строки с пометкой «Отключено».", "ASA", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string list = string.Join("\n", selected.Select(row =>
        {
            var item = (SurveillanceItem)row.Tag!;
            return "• " + (disable ? item.DisableLabel : item.EnableLabel) + ": " + item.Title;
        }).Take(15));
        if (MessageBox.Show("Сделать?\n\n" + list, "ASA: аудит слежки", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        foreach (ListViewItem row in selected)
        {
            var item = (SurveillanceItem)row.Tag!;
            try
            {
                string message = disable ? item.Disable!() : item.Enable!();
                Host.Journal.Information($"Аудит слежки, {(disable ? item.DisableLabel : item.EnableLabel)}: {item.Title}. {message}");
                row.SubItems[0].Text = disable ? "Отключено" : "Включено";
                row.SubItems[3].Text = message;
                row.ForeColor = disable ? Theme.Success : Theme.Text;
                row.Tag = null;
            }
            catch (Exception exception)
            {
                Host.Journal.Warning($"Аудит слежки, не удалось изменить «{item.Title}»: {exception.Message}");
                row.SubItems[3].Text = "Не удалось: " + exception.Message;
            }
        }

        auditList.Invalidate();
        auditStatusLabel.Text = "Изменения применены. Запустите проверку снова, чтобы увидеть новое состояние.";
        auditStatusLabel.ForeColor = Theme.Text;
    }

    private void ShowAuditDetails()
    {
        if (auditList.SelectedItems.Count == 0)
        {
            return;
        }

        ListViewItem row = auditList.SelectedItems[0];
        string text = row.SubItems[2].Text + "\n\n" + row.SubItems[3].Text;
        if (row.Tag is SurveillanceItem item)
        {
            if (item.DisableLabel != null)
            {
                text += "\n\nМожно: " + item.DisableLabel;
            }

            if (item.EnableLabel != null)
            {
                text += "\n\nМожно: " + item.EnableLabel;
            }
        }

        MessageBox.Show(text, "ASA: подробности", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
