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
        Height = 210;
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
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        Ornaments.FillBackgroundFor(this, graphics, ClientRectangle, Ornaments.DamaskTile);
        Ornaments.DrawGlassPanel(graphics, ClientRectangle, Theme.Card, 12);

        Color color = level switch
        {
            ProtectionLevel.Protected => Theme.Success,
            ProtectionLevel.Loading => Theme.Warning,
            _ => Theme.Danger
        };

        var center = new PointF(112, Height / 2f + 4);

        using (var halo = new GraphicsPath())
        {
            halo.AddEllipse(center.X - 86, center.Y - 86, 172, 172);
            using var haloBrush = new PathGradientBrush(halo) { CenterColor = Color.FromArgb(90, color), SurroundColors = new[] { Color.FromArgb(0, color) } };
            graphics.FillPath(haloBrush, halo);
        }

        float ringRadius = 60 + pulse * 18;
        int ringAlpha = (int)(120 * (1 - pulse));
        using (var ringPen = new Pen(Color.FromArgb(ringAlpha, Theme.PaleGold), 2))
        {
            graphics.DrawEllipse(ringPen, center.X - ringRadius, center.Y - ringRadius, ringRadius * 2, ringRadius * 2);
        }

        if (level == ProtectionLevel.Loading)
        {
            using var spinnerPen = new Pen(Theme.PaleGold, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(spinnerPen, center.X - 64, center.Y - 64, 128, 128, pulse * 360, 90);
        }

        using GraphicsPath shield = CreateShieldPath(center, 52);
        var shieldBounds = shield.GetBounds();
        using (var shieldShadow = (GraphicsPath)shield.Clone())
        {
            using var shadowMatrix = new Matrix();
            shadowMatrix.Translate(3, 5);
            shieldShadow.Transform(shadowMatrix);
            using var shadowBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
            graphics.FillPath(shadowBrush, shieldShadow);
        }

        using (var body = new PathGradientBrush(shield)
        {
            CenterPoint = new PointF(center.X - 10, center.Y - 22),
            CenterColor = Lighten(color, 0.35f),
            SurroundColors = new[] { Darken(color, 0.45f) }
        })
        {
            graphics.FillPath(body, shield);
        }

        Ornaments.DrawImperialEagle(graphics, new PointF(center.X, center.Y + 2), 62, new SolidBrush(Color.FromArgb(235, 255, 236, 180)));

        var gloss = new RectangleF(shieldBounds.X + 6, shieldBounds.Y + 4, shieldBounds.Width - 12, shieldBounds.Height * 0.46f);
        using (var glossPath = new GraphicsPath())
        {
            glossPath.AddBezier(gloss.Left, gloss.Top + 8, gloss.Left + gloss.Width * 0.3f, gloss.Top - 4, gloss.Right - gloss.Width * 0.3f, gloss.Top - 4, gloss.Right, gloss.Top + 8);
            glossPath.AddBezier(gloss.Right, gloss.Top + 8, gloss.Right, gloss.Bottom * 0.98f, gloss.Left + gloss.Width * 0.6f, gloss.Bottom, gloss.Left, gloss.Bottom);
            glossPath.CloseFigure();
            using var glossBrush = new LinearGradientBrush(gloss, Color.FromArgb(130, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical);
            graphics.SetClip(shield);
            graphics.FillPath(glossBrush, glossPath);
            graphics.ResetClip();
        }

        using (LinearGradientBrush rim = Ornaments.CreateMetallicGoldBrush(shieldBounds))
        using (var rimPen = new Pen(rim, 4f) { LineJoin = LineJoin.Round })
        {
            graphics.DrawPath(rimPen, shield);
        }

        using (var innerRim = new Pen(Color.FromArgb(120, Color.White), 1f) { LineJoin = LineJoin.Round })
        using (GraphicsPath inner = CreateShieldPath(center, 46))
        {
            graphics.DrawPath(innerRim, inner);
        }

        float textLeft = 224;
        Ornaments.DrawSpacedText(graphics, title.ToUpperInvariant(), Theme.StatusTitleFont, new PointF(textLeft, Height / 2f - 40), 2.5f);
        using (var statusDot = new SolidBrush(Lighten(color, 0.2f)))
        {
            graphics.FillEllipse(statusDot, textLeft + 1, Height / 2f + 9, 10, 10);
        }

        TextRenderer.DrawText(graphics, subtitle, Theme.BodyFont, new Rectangle((int)textLeft + 18, Height / 2 + 4, Width - (int)textLeft - 40, 60), Theme.Text, TextFormatFlags.WordBreak);
    }

    private static Color Lighten(Color color, float amount)
    {
        return Color.FromArgb(color.A, (int)(color.R + (255 - color.R) * amount), (int)(color.G + (255 - color.G) * amount), (int)(color.B + (255 - color.B) * amount));
    }

    private static Color Darken(Color color, float amount)
    {
        return Color.FromArgb(color.A, (int)(color.R * (1 - amount)), (int)(color.G * (1 - amount)), (int)(color.B * (1 - amount)));
    }

    private static GraphicsPath CreateShieldPath(PointF center, float size)
    {
        var path = new GraphicsPath();
        path.AddLines(new[]
        {
            new PointF(center.X - size * 0.92f, center.Y - size * 0.95f),
            new PointF(center.X - size * 0.3f, center.Y - size * 0.82f),
            new PointF(center.X, center.Y - size * 1.05f),
            new PointF(center.X + size * 0.3f, center.Y - size * 0.82f),
            new PointF(center.X + size * 0.92f, center.Y - size * 0.95f)
        });
        path.AddBezier(center.X + size * 0.92f, center.Y - size * 0.95f, center.X + size * 0.95f, center.Y + size * 0.2f, center.X + size * 0.6f, center.Y + size * 0.8f, center.X, center.Y + size * 1.12f);
        path.AddBezier(center.X, center.Y + size * 1.12f, center.X - size * 0.6f, center.Y + size * 0.8f, center.X - size * 0.95f, center.Y + size * 0.2f, center.X - size * 0.92f, center.Y - size * 0.95f);
        path.CloseFigure();
        return path;
    }
}
