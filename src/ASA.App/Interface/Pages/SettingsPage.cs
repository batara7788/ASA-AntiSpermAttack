using Asa.App.Services;

namespace Asa.App.Interface.Pages;

public sealed class SettingsPage : PageBase
{
    private readonly CheckBox realTimeCheckBox;
    private readonly CheckBox usbCheckBox;
    private readonly CheckBox processCheckBox;
    private readonly CheckBox autorunCheckBox;
    private readonly CheckBox ransomwareCheckBox;
    private readonly CheckBox gameModeCheckBox;
    private readonly CheckBox soundsCheckBox;
    private readonly CheckBox scheduleCheckBox;
    private readonly ComboBox dayComboBox = CreateComboBox(160);
    private readonly NumericUpDown hourNumeric = new() { Minimum = 0, Maximum = 23, Width = 70, BackColor = Theme.Card, ForeColor = Theme.Text };
    private readonly NumericUpDown sizeNumeric = new() { Minimum = 10, Maximum = 2000, Width = 90, BackColor = Theme.Card, ForeColor = Theme.Text };
    private readonly ListBox foldersList = CreateListBox();
    private readonly ListBox exclusionsList = CreateListBox();
    private readonly Button contextMenuButton = Theme.CreateButton(string.Empty);
    private readonly Button autostartButton = Theme.CreateButton(string.Empty);
    private readonly Label databaseLabel = Theme.CreateLabel(string.Empty, Theme.SmallFont, Theme.MutedText);

    public SettingsPage(ProtectionHost host)
        : base(host, "Настройки", "Что и как защищает ASA", PageIcon.Gear)
    {
        realTimeCheckBox = Theme.CreateCheckBox("Защита в реальном времени (проверка новых и изменённых файлов)", host.Settings.RealTimeProtectionEnabled);
        usbCheckBox = Theme.CreateCheckBox("Проверять флешки при подключении", host.Settings.UsbScanEnabled);
        processCheckBox = Theme.CreateCheckBox("Проверять каждую запускаемую программу", host.Settings.ProcessScanEnabled);
        autorunCheckBox = Theme.CreateCheckBox("Следить за автозагрузкой", host.Settings.AutorunMonitorEnabled);
        ransomwareCheckBox = Theme.CreateCheckBox("Защита от шифровальщиков (файлы-ловушки и слежка за массовыми изменениями)", host.Settings.RansomwareProtectionEnabled);
        gameModeCheckBox = Theme.CreateCheckBox("Игровой режим: не мешать в играх и полноэкранных фильмах", host.Settings.GameModeEnabled);
        soundsCheckBox = Theme.CreateCheckBox("Звуки: фанфары при запуске, сигнал при угрозе, перезвон после проверки", host.Settings.SoundsEnabled);
        scheduleCheckBox = Theme.CreateCheckBox("Полная проверка по расписанию", host.Settings.ScheduledScanEnabled);

        foreach (CheckBox checkBox in new[] { realTimeCheckBox, usbCheckBox, processCheckBox, autorunCheckBox, ransomwareCheckBox, gameModeCheckBox, soundsCheckBox, scheduleCheckBox })
        {
            checkBox.CheckedChanged += (_, _) => SaveModules();
        }

        string[] dayNames = { "воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота" };
        dayComboBox.Items.AddRange(dayNames);
        dayComboBox.SelectedIndex = (int)host.Settings.ScheduledScanDay;
        hourNumeric.Value = host.Settings.ScheduledScanHour;
        sizeNumeric.Value = host.Settings.MaximumFileSizeMegabytes;
        dayComboBox.SelectedIndexChanged += (_, _) => SaveSchedule();
        hourNumeric.ValueChanged += (_, _) => SaveSchedule();
        sizeNumeric.ValueChanged += (_, _) =>
        {
            Host.Settings.MaximumFileSizeMegabytes = (long)sizeNumeric.Value;
            Host.SaveSettings();
        };

        Content.Controls.Add(Theme.CreateLabel("Модули", Theme.SubtitleFont));
        Content.Controls.AddRange(new Control[] { realTimeCheckBox, usbCheckBox, processCheckBox, autorunCheckBox, ransomwareCheckBox, gameModeCheckBox, soundsCheckBox, scheduleCheckBox });

        FlowLayoutPanel scheduleRow = Theme.CreateButtonRow();
        scheduleRow.Controls.Add(new Label { Text = "День:", AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = Theme.BodyFont, Margin = new Padding(0, 8, 8, 0) });
        scheduleRow.Controls.Add(new GoldFrame(dayComboBox, 170));
        scheduleRow.Controls.Add(new Label { Text = "Час:", AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = Theme.BodyFont, Margin = new Padding(0, 8, 8, 0) });
        scheduleRow.Controls.Add(new GoldFrame(hourNumeric, 80));
        scheduleRow.Controls.Add(new Label { Text = "Не проверять файлы больше (МБ):", AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Font = Theme.BodyFont, Margin = new Padding(0, 8, 8, 0) });
        scheduleRow.Controls.Add(new GoldFrame(sizeNumeric, 100));
        Content.Controls.Add(scheduleRow);

        Content.Controls.Add(Theme.CreateLabel("Папки под защитой в реальном времени", Theme.SubtitleFont));
        Content.Controls.Add(foldersList);
        Content.Controls.Add(CreateListButtons(foldersList, host.Settings.ProtectedFolders, true));

        Content.Controls.Add(Theme.CreateLabel("Исключения (эти папки и файлы не проверяются)", Theme.SubtitleFont));
        Content.Controls.Add(exclusionsList);
        Content.Controls.Add(CreateListButtons(exclusionsList, host.Settings.ExcludedPaths, false));

        Content.Controls.Add(Theme.CreateLabel("Интеграция с Windows", Theme.SubtitleFont));
        contextMenuButton.Click += (_, _) => ToggleContextMenu();
        autostartButton.Click += (_, _) => ToggleAutostart();
        var updateDatabaseButton = Theme.CreateButton("Обновить вирусные базы сейчас");
        updateDatabaseButton.Click += async (_, _) =>
        {
            updateDatabaseButton.Enabled = false;
            databaseLabel.Text = "Обновляю базы...";
            bool updated = await Task.Run(() => Host.Engine.UpdateDatabaseAsync(CancellationToken.None));
            if (updated)
            {
                Host.Sounds.Play(SoundEvent.DatabaseUpdated);
                Host.Settings.LastDatabaseUpdate = DateTime.Now;
                Host.SaveSettings();
            }

            updateDatabaseButton.Enabled = true;
            RefreshView();
        };

        FlowLayoutPanel integrationRow = Theme.CreateButtonRow();
        integrationRow.Controls.AddRange(new Control[] { contextMenuButton, autostartButton, updateDatabaseButton });
        Content.Controls.Add(integrationRow);
        Content.Controls.Add(databaseLabel);
    }

    public override void OnShown()
    {
        RefreshView();
    }

    private void RefreshView()
    {
        FillList(foldersList, Host.Settings.ProtectedFolders);
        FillList(exclusionsList, Host.Settings.ExcludedPaths);
        contextMenuButton.Text = SystemIntegration.IsContextMenuInstalled() ? "Убрать «Проверить с помощью ASA» из меню" : "Добавить «Проверить с помощью ASA» в меню правой кнопки";
        autostartButton.Text = SystemIntegration.IsAutostartInstalled() ? "Не запускать ASA вместе с Windows" : "Запускать ASA вместе с Windows";
        string lastUpdate = Host.Settings.LastDatabaseUpdate == DateTime.MinValue ? "никогда" : Host.Settings.LastDatabaseUpdate.ToString("dd.MM.yyyy HH:mm");
        databaseLabel.Text = $"Движок: {Host.Engine.Version}. Базы обновлялись: {lastUpdate}. Данные ASA: {Host.Folders.RootFolder}";
    }

    private void SaveModules()
    {
        Host.Settings.RealTimeProtectionEnabled = realTimeCheckBox.Checked;
        Host.Settings.UsbScanEnabled = usbCheckBox.Checked;
        Host.Settings.ProcessScanEnabled = processCheckBox.Checked;
        Host.Settings.AutorunMonitorEnabled = autorunCheckBox.Checked;
        Host.Settings.RansomwareProtectionEnabled = ransomwareCheckBox.Checked;
        Host.Settings.GameModeEnabled = gameModeCheckBox.Checked;
        Host.Settings.SoundsEnabled = soundsCheckBox.Checked;
        Host.Settings.ScheduledScanEnabled = scheduleCheckBox.Checked;
        Host.SaveSettings();
        Host.ApplyModuleSettings();
    }

    private void SaveSchedule()
    {
        Host.Settings.ScheduledScanDay = (DayOfWeek)dayComboBox.SelectedIndex;
        Host.Settings.ScheduledScanHour = (int)hourNumeric.Value;
        Host.SaveSettings();
    }

    private FlowLayoutPanel CreateListButtons(ListBox listBox, List<string> target, bool foldersOnly)
    {
        var addFolderButton = Theme.CreateButton("+ Папка");
        var addFileButton = Theme.CreateButton("+ Файл");
        var removeButton = Theme.CreateButton("− Убрать");

        addFolderButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK && !target.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(dialog.SelectedPath);
                CommitList(listBox, target);
            }
        };

        addFileButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK && !target.Contains(dialog.FileName, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(dialog.FileName);
                CommitList(listBox, target);
            }
        };

        removeButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is string selected)
            {
                target.Remove(selected);
                CommitList(listBox, target);
            }
        };

        FlowLayoutPanel row = Theme.CreateButtonRow();
        row.Controls.Add(addFolderButton);
        if (!foldersOnly)
        {
            row.Controls.Add(addFileButton);
        }

        row.Controls.Add(removeButton);
        return row;
    }

    private void CommitList(ListBox listBox, List<string> target)
    {
        Host.SaveSettings();
        FillList(listBox, target);
        Host.ApplyModuleSettings();
    }

    private static void FillList(ListBox listBox, List<string> items)
    {
        listBox.BeginUpdate();
        listBox.Items.Clear();
        foreach (string item in items)
        {
            listBox.Items.Add(item);
        }

        listBox.EndUpdate();
    }

    private void ToggleContextMenu()
    {
        if (SystemIntegration.IsContextMenuInstalled())
        {
            SystemIntegration.RemoveContextMenu();
        }
        else
        {
            SystemIntegration.InstallContextMenu();
        }

        RefreshView();
    }

    private void ToggleAutostart()
    {
        bool success = SystemIntegration.IsAutostartInstalled() ? SystemIntegration.RemoveAutostart() : SystemIntegration.InstallAutostart();
        if (!success)
        {
            MessageBox.Show("Не удалось изменить автозапуск. Нужны права администратора.", "ASA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshView();
    }

    private static ComboBox CreateComboBox(int width)
    {
        return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, BackColor = Theme.Card, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 14, 8) };
    }

    private static ListBox CreateListBox()
    {
        return new ListBox { Width = 720, Height = 130, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.BodyFont, Margin = new Padding(0, 0, 0, 6) };
    }
}
