using Asa.App.Services;
using Asa.Core;

namespace Asa.App.Interface.Pages;

public sealed class JournalPage : PageBase
{
    private readonly ListView entriesList = Theme.CreateListView(("Время", 150), ("Тип", 110), ("Событие", 800));
    private readonly CheckBox threatsOnlyCheckBox = Theme.CreateCheckBox("Только угрозы и предупреждения", false);

    public JournalPage(ProtectionHost host)
        : base(host, "Журнал", "Всё, что делал ASA")
    {
        threatsOnlyCheckBox.CheckedChanged += (_, _) => Reload();
        var openFolderButton = Theme.CreateButton("Открыть папку с логами");
        openFolderButton.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", "\"" + host.Folders.LogsFolder + "\"");

        FlowLayoutPanel row = Theme.CreateButtonRow();
        row.Controls.AddRange(new Control[] { threatsOnlyCheckBox, openFolderButton });

        Panel listPanel = CreateFillPanel(500);
        listPanel.Controls.Add(entriesList);

        Content.Controls.Add(row);
        Content.Controls.Add(listPanel);

        host.Journal.EntryAdded += entry => RunOnInterface(() =>
        {
            if (Visible && ShouldShow(entry))
            {
                entriesList.Items.Insert(0, CreateRow(entry));
            }
        });
    }

    public override void OnShown()
    {
        Reload();
    }

    private bool ShouldShow(JournalEntry entry)
    {
        return !threatsOnlyCheckBox.Checked || entry.Level != JournalLevel.Information;
    }

    private void Reload()
    {
        entriesList.BeginUpdate();
        entriesList.Items.Clear();
        foreach (JournalEntry entry in Host.Journal.GetEntries().Reverse().Where(ShouldShow).Take(1000))
        {
            entriesList.Items.Add(CreateRow(entry));
        }

        entriesList.EndUpdate();
    }

    private static ListViewItem CreateRow(JournalEntry entry)
    {
        (string text, Color color) = entry.Level switch
        {
            JournalLevel.Threat => ("Угроза", Theme.Danger),
            JournalLevel.Warning => ("Внимание", Theme.Warning),
            _ => ("Инфо", Theme.Text)
        };

        return new ListViewItem(new[] { entry.Time.ToString("dd.MM HH:mm:ss"), text, entry.Message }) { ForeColor = color };
    }
}
