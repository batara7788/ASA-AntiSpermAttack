using System.Drawing.Drawing2D;
using Asa.Core;

namespace Asa.App.Interface.Controls;

public sealed class ActivityChart : Control
{
    private IReadOnlyList<DailyStatistics> days = Array.Empty<DailyStatistics>();

    public ActivityChart()
    {
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Height = 220;
        Margin = new Padding(0, 0, 0, 16);
    }

    public void SetData(IReadOnlyList<DailyStatistics> data)
    {
        days = data;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Ornaments.FillBackgroundFor(this, graphics, ClientRectangle, Ornaments.DamaskTile);
        Ornaments.DrawOrnateFrame(graphics, ClientRectangle, Theme.Gold);

        TextRenderer.DrawText(graphics, "Активность за 14 дней", Theme.SubtitleFont, new Point(30, 16), Theme.PaleGold);
        TextRenderer.DrawText(graphics, "столбец — проверено файлов, рубин — угрозы", Theme.SmallFont, new Point(Width - 300, 22), Theme.MutedText);

        if (days.Count == 0)
        {
            return;
        }

        var area = new Rectangle(30, 52, Width - 60, Height - 88);
        long maximum = Math.Max(1, days.Max(day => day.FilesScanned));
        float slot = area.Width / (float)days.Count;
        float barWidth = Math.Max(4, slot * 0.6f);

        using var gridPen = new Pen(Theme.Border) { DashStyle = DashStyle.Dot };
        for (int line = 0; line <= 3; line++)
        {
            float y = area.Bottom - area.Height * line / 3f;
            graphics.DrawLine(gridPen, area.Left, y, area.Right, y);
        }

        for (int index = 0; index < days.Count; index++)
        {
            DailyStatistics day = days[index];
            float x = area.Left + slot * index + (slot - barWidth) / 2;
            float barHeight = area.Height * day.FilesScanned / (float)maximum;
            var bar = new RectangleF(x, area.Bottom - barHeight, barWidth, Math.Max(barHeight, 1));

            if (bar.Height > 2)
            {
                using var gradient = new LinearGradientBrush(bar, Theme.PaleGold, Theme.Border, LinearGradientMode.Vertical);
                graphics.FillRectangle(gradient, bar);
            }

            if (day.ThreatsFound > 0)
            {
                using var threatBrush = new SolidBrush(Theme.Danger);
                graphics.FillEllipse(threatBrush, x + barWidth / 2 - 5, bar.Top - 14, 10, 10);
                TextRenderer.DrawText(graphics, day.ThreatsFound.ToString(), Theme.SmallFont, new Point((int)(x + barWidth / 2 + 6), (int)bar.Top - 18), Theme.Danger);
            }

            if (index % 2 == 0 || days.Count <= 7)
            {
                TextRenderer.DrawText(graphics, day.Day.ToString("dd.MM"), Theme.SmallFont, new Point((int)(x - 4), area.Bottom + 6), Theme.MutedText);
            }
        }
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        Invalidate();
    }
}
