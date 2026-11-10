using Asa.App.Services;
using Asa.Core;

namespace Asa.App.Interface.Pages;

public sealed class AutorunPage : PageBase
{
    private readonly ListView entriesList = Theme.CreateListView(("Имя", 220), ("Команда", 520), ("Где", 260));

    public AutorunPage(ProtectionHost host)
        : base(host, "Автозагрузка", "Программы, которые запускаются вместе с Windows. ASA предупреждает о каждой новой")
    {
        var refreshButton = Theme.CreateButton("Обновить");
        var removeButton = Theme.CreateButton("Убрать из автозагрузки");
        var scanButton = Theme.CreateButton("Проверить выбранное");
        refreshButton.Click += (_, _) => Reload();
        removeButton.Click += (_, _) => RemoveSelected();
        scanButton.Click += async (_, _) => await ScanSelectedAsync();

        FlowLayoutPanel buttons = Theme.CreateButtonRow();
        buttons.Controls.AddRange(new Control[] { refreshButton, removeButton, scanButton });

        Panel listPanel = CreateFillPanel(460);
        listPanel.Controls.Add(entriesList);

        Content.Controls.Add(buttons);
        Content.Controls.Add(listPanel);

        host.Autoruns.EntriesChanged += () => RunOnInterface(Reload);
    }

    public override void OnShown()
    {
        Reload();
    }

    private void Reload()
    {
        entriesList.BeginUpdate();
        entriesList.Items.Clear();
        foreach (AutorunEntry entry in AutorunMonitor.ReadAllEntries())
        {
            entriesList.Items.Add(new ListViewItem(new[] { entry.Name, entry.Command, entry.Location }) { Tag = entry });
        }

        entriesList.EndUpdate();
    }

    private void RemoveSelected()
    {
        List<AutorunEntry> selected = entriesList.SelectedItems.Cast<ListViewItem>().Select(row => (AutorunEntry)row.Tag!).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        string names = string.Join("\n", selected.Select(entry => "• " + entry.Name));
        if (MessageBox.Show("Убрать из автозагрузки?\n\n" + names, "ASA", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        foreach (AutorunEntry entry in selected)
        {
            Host.Autoruns.RemoveEntry(entry);
        }

        Reload();
    }

    private async Task ScanSelectedAsync()
    {
        foreach (ListViewItem row in entriesList.SelectedItems)
        {
            var entry = (AutorunEntry)row.Tag!;
            string? executable = AutorunSnapshot.ExtractExecutablePath(entry.Command);
            if (executable == null || !File.Exists(executable))
            {
                row.ForeColor = Theme.MutedText;
                continue;
            }

            ScanResult result = await Task.Run(() => Host.Scanner.ScanAndHandleAsync(executable, "Проверка автозагрузки", CancellationToken.None));
            row.ForeColor = result.Verdict switch
            {
                ScanVerdict.Clean => Theme.Success,
                ScanVerdict.Infected => Theme.Danger,
                _ => Theme.Warning
            };
        }

        entriesList.Invalidate();
    }
}
