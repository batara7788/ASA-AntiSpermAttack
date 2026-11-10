namespace Asa.App.Services;

public sealed class Notifier
{
    private readonly GameModeDetector gameMode;
    private readonly object queueLock = new();
    private readonly List<(string Title, string Text, ToolTipIcon Icon)> postponed = new();
    private NotifyIcon? trayIcon;

    public Notifier(GameModeDetector gameMode)
    {
        this.gameMode = gameMode;
    }

    public void Attach(NotifyIcon icon)
    {
        trayIcon = icon;
    }

    public void Show(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (gameMode.IsUserBusy())
        {
            lock (queueLock)
            {
                postponed.Add((title, text, icon));
            }

            return;
        }

        trayIcon?.ShowBalloonTip(6000, title, text, icon);
    }

    public void FlushPostponedIfFree()
    {
        if (gameMode.IsUserBusy())
        {
            return;
        }

        List<(string Title, string Text, ToolTipIcon Icon)> items;
        lock (queueLock)
        {
            if (postponed.Count == 0)
            {
                return;
            }

            items = postponed.ToList();
            postponed.Clear();
        }

        if (items.Count == 1)
        {
            trayIcon?.ShowBalloonTip(6000, items[0].Title, items[0].Text, items[0].Icon);
            return;
        }

        bool anyThreat = items.Any(item => item.Icon == ToolTipIcon.Error);
        string summary = $"Пока вы играли, накопилось уведомлений: {items.Count}. Подробности в журнале.";
        trayIcon?.ShowBalloonTip(8000, "ASA: AntiSpermAttack", summary, anyThreat ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }
}
