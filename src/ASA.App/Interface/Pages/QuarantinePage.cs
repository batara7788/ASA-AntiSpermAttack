using Asa.App.Services;
using Asa.Core;

namespace Asa.App.Interface.Pages;

public sealed class QuarantinePage : PageBase
{
    private readonly ListView itemsList = Theme.CreateListView(("Файл", 420), ("Угроза", 260), ("Когда", 150), ("Размер", 100));

    public QuarantinePage(ProtectionHost host)
        : base(host, "Карантин", "Обезвреженные файлы хранятся здесь в зашифрованном виде и не могут запуститься", PageIcon.Chest)
    {
        var restoreButton = Theme.CreateButton("Восстановить");
        var deleteButton = Theme.CreateButton("Удалить навсегда");
        var deleteAllButton = Theme.CreateButton("Очистить карантин");
        restoreButton.Click += (_, _) => RestoreSelected();
        deleteButton.Click += (_, _) => DeleteSelected();
        deleteAllButton.Click += (_, _) => DeleteAll();

        FlowLayoutPanel buttons = Theme.CreateButtonRow();
        buttons.Controls.AddRange(new Control[] { restoreButton, deleteButton, deleteAllButton });

        Panel listPanel = CreateFillPanel(460);
        itemsList.MultiSelect = true;
        listPanel.Controls.Add(itemsList);

        Content.Controls.Add(buttons);
        Content.Controls.Add(listPanel);

        host.Quarantine.Changed += () => RunOnInterface(Reload);
    }

    public override void OnShown()
    {
        Reload();
    }

    private void Reload()
    {
        itemsList.BeginUpdate();
        itemsList.Items.Clear();
        foreach (QuarantineItem item in Host.Quarantine.GetItems())
        {
            var row = new ListViewItem(new[] { item.OriginalPath, item.ThreatName, item.QuarantinedAt.ToString("dd.MM.yyyy HH:mm"), Theme.FormatBytes(item.OriginalSize) })
            {
                Tag = item,
                ForeColor = Theme.Warning
            };
            itemsList.Items.Add(row);
        }

        itemsList.EndUpdate();
    }

    private List<QuarantineItem> GetSelected()
    {
        return itemsList.SelectedItems.Cast<ListViewItem>().Select(row => (QuarantineItem)row.Tag!).ToList();
    }

    private void RestoreSelected()
    {
        List<QuarantineItem> selected = GetSelected();
        if (selected.Count == 0)
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            "Восстановленный файл может навредить компьютеру. Восстанавливайте, только если уверены, что это ложная тревога.\n\nВосстановить и добавить в исключения?",
            "Восстановление из карантина",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        foreach (QuarantineItem item in selected)
        {
            try
            {
                string restoredPath = Host.Quarantine.Restore(item.Id);
                Host.Settings.ExcludedPaths.Add(restoredPath);
                Host.Journal.Information("Восстановлен из карантина и добавлен в исключения: " + restoredPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                MessageBox.Show("Не удалось восстановить " + item.OriginalPath + ": " + exception.Message, "ASA", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        Host.SaveSettings();
    }

    private void DeleteSelected()
    {
        foreach (QuarantineItem item in GetSelected())
        {
            Host.Quarantine.Delete(item.Id);
            Host.Journal.Information("Удалён из карантина: " + item.OriginalPath);
        }
    }

    private void DeleteAll()
    {
        if (MessageBox.Show("Удалить все файлы из карантина навсегда?", "ASA", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        foreach (QuarantineItem item in Host.Quarantine.GetItems())
        {
            Host.Quarantine.Delete(item.Id);
        }

        Host.Journal.Information("Карантин очищен");
    }
}
