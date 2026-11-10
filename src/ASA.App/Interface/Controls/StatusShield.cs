using System.Drawing.Drawing2D;

namespace Asa.App.Interface.Controls;

public enum ProtectionLevel
{
    Protected,
    Loading,
    AtRisk
}

public sealed class StatusShield : Control
{
    private ProtectionLevel level = ProtectionLevel.Loading;
    private string title = string.Empty;
    private string subtitle = string.Empty;
    private float pulse;
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 40 };

    public StatusShield()
    {
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Height = 200;
        animationTimer.Tick += (_, _) =>
        {
            pulse = (pulse + 0.04f) % 1f;
            Invalidate();
        };
        animationTimer.Start();
    }

    public void SetStatus(ProtectionLevel newLevel, string newTitle, string newSubtitle)
    {
        level = newLevel;
        title = newTitle;
        subtitle = newSubtitle;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Theme.Card);
        Ornaments.DrawOrnateFrame(graphics, ClientRectangle, Theme.Gold);

        Color color = level switch
        {
            ProtectionLevel.Protected => Theme.Success,
            ProtectionLevel.Loading => Theme.Warning,
            _ => Theme.Danger
        };

        var center = new PointF(100, Height / 2f);
        float ringRadius = 52 + pulse * 22;
        int ringAlpha = (int)(110 * (1 - pulse));
        using (var ringPen = new Pen(Color.FromArgb(ringAlpha, color), 3))
        {
            graphics.DrawEllipse(ringPen, center.X - ringRadius, center.Y - ringRadius, ringRadius * 2, ringRadius * 2);
        }

        using GraphicsPath shield = CreateShieldPath(center, 48);
        using (var fill = new LinearGradientBrush(new RectangleF(center.X - 48, center.Y - 54, 96, 108), color, Color.FromArgb(80, color), LinearGradientMode.Vertical))
        {
            graphics.FillPath(fill, shield);
        }

        using (var rimPen = new Pen(Theme.Gold, 3f))
        {
            graphics.DrawPath(rimPen, shield);
        }

        Ornaments.DrawCrown(graphics, new RectangleF(center.X - 18, center.Y - 84, 36, 24), Theme.Gold);

        using (var markPen = new Pen(Color.White, 6) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            if (level == ProtectionLevel.Protected)
            {
                graphics.DrawLines(markPen, new[] { new PointF(center.X - 18, center.Y), new PointF(center.X - 4, center.Y + 15), new PointF(center.X + 20, center.Y - 14) });
            }
            else if (level == ProtectionLevel.AtRisk)
            {
                graphics.DrawLine(markPen, center.X, center.Y - 20, center.X, center.Y + 4);
                graphics.DrawLine(markPen, center.X, center.Y + 17, center.X, center.Y + 18);
            }
            else
            {
                float angle = pulse * 360;
                graphics.DrawArc(markPen, center.X - 16, center.Y - 16, 32, 32, angle, 260);
            }
        }

        TextRenderer.DrawText(graphics, title, Theme.TitleFont, new Point(190, Height / 2 - 36), Theme.PaleGold);
        TextRenderer.DrawText(graphics, subtitle, Theme.BodyFont, new Rectangle(192, Height / 2 + 6, Width - 210, 50), Theme.MutedText, TextFormatFlags.WordBreak);
    }

    private static GraphicsPath CreateShieldPath(PointF center, float size)
    {
        var path = new GraphicsPath();
        path.AddLines(new[]
        {
            new PointF(center.X, center.Y - size * 1.1f),
            new PointF(center.X + size, center.Y - size * 0.75f),
            new PointF(center.X + size * 0.9f, center.Y + size * 0.25f),
            new PointF(center.X, center.Y + size * 1.1f),
            new PointF(center.X - size * 0.9f, center.Y + size * 0.25f),
            new PointF(center.X - size, center.Y - size * 0.75f)
        });
        path.CloseFigure();
        return path;
    }
}
