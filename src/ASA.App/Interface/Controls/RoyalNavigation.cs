using System.Drawing.Drawing2D;

namespace Asa.App.Interface;

public sealed class RoyalSidebar : FlowLayoutPanel
{
    public RoyalSidebar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Sidebar;
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        Ornaments.FillBackgroundFor(this, graphics, ClientRectangle, Ornaments.SidebarTile);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var stripe = new RectangleF(Width - 16, 0, 14, Height);
        using (var stripeBrush = new SolidBrush(Color.FromArgb(160, 4, 9, 26)))
        {
            graphics.FillRectangle(stripeBrush, stripe);
        }

        using (LinearGradientBrush gold = Ornaments.CreateMetallicGoldBrush(new RectangleF(0, 0, Width, 40), false))
        using (var goldPen = new Pen(gold, 2f))
        {
            graphics.DrawLine(goldPen, stripe.Left, 0, stripe.Left, Height);
            graphics.DrawLine(goldPen, stripe.Right, 0, stripe.Right, Height);
        }

        DrawLaurelGarland(graphics, stripe.Left + stripe.Width / 2, 0, Height);

        int bottomY = Height - 92;
        var palmetteBrush = new SolidBrush(Color.FromArgb(200, Theme.Gold));
        Ornaments.DrawPalmette(graphics, palmetteBrush, new PointF((Width - 16) / 2f, bottomY + 46), 40);
        palmetteBrush.Dispose();
        Ornaments.DrawMeanderBand(graphics, new RectangleF(14, bottomY + 56, Width - 46, 14), Color.FromArgb(200, Theme.Gold));
    }

    private static void DrawLaurelGarland(Graphics graphics, float centerX, float top, float bottom)
    {
        using var leafBrush = new SolidBrush(Color.FromArgb(230, Theme.Gold));
        using var darkLeafBrush = new SolidBrush(Color.FromArgb(230, 176, 136, 44));
        using var stemPen = new Pen(Color.FromArgb(200, Theme.Gold), 1f);
        graphics.DrawLine(stemPen, centerX, top, centerX, bottom);

        const float step = 9f;
        int index = 0;
        for (float y = top + 6; y < bottom; y += step, index++)
        {
            int side = index % 2 == 0 ? -1 : 1;
            Brush brush = index % 4 < 2 ? leafBrush : darkLeafBrush;
            Ornaments.DrawLeaf(graphics, brush, new PointF(centerX, y), new PointF(centerX + side * 5.2f, y - 6.5f), 1.9f);

            if (index % 12 == 6)
            {
                graphics.FillEllipse(leafBrush, centerX - 2.4f, y - 2.4f, 4.8f, 4.8f);
                using var berryShine = new SolidBrush(Color.FromArgb(160, Color.White));
                graphics.FillEllipse(berryShine, centerX - 1.2f, y - 1.6f, 1.4f, 1.4f);
            }
        }
    }
}

public sealed class NavigationItem : Control
{
    private readonly string numeral;
    private bool selected;
    private bool hovered;

    public NavigationItem(string numeral, string text)
    {
        this.numeral = numeral;
        Text = text;
        Size = new Size(204, 44);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Ornaments.FillBackgroundFor(this, eventArgs.Graphics, ClientRectangle, Ornaments.SidebarTile);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new Rectangle(1, 2, Width - 3, Height - 5);

        if (selected)
        {
            Ornaments.DrawGlassPanel(graphics, area, Color.FromArgb(34, 58, 128), 8, false);
        }
        else if (hovered)
        {
            Ornaments.DrawGlassPanel(graphics, area, Color.FromArgb(20, 36, 84), 8, false);
        }

        Color numeralColor = selected ? Theme.PaleGold : Color.FromArgb(170, Theme.Gold);
        TextRenderer.DrawText(graphics, numeral, Theme.NumeralFont, new Rectangle(6, 0, 40, Height), numeralColor, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

        using (var separator = new Pen(Color.FromArgb(selected ? 200 : 90, Theme.Gold)))
        {
            graphics.DrawLine(separator, 50, 13, 50, Height - 13);
        }

        Color textColor = selected ? Color.White : hovered ? Theme.Text : Theme.MutedText;
        TextRenderer.DrawText(graphics, Text, Theme.NavigationFont, new Rectangle(60, 0, Width - 62, Height), textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

public sealed class RoyalLogo : Control
{
    public RoyalLogo()
    {
        Size = new Size(204, 176);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Ornaments.FillBackgroundFor(this, eventArgs.Graphics, ClientRectangle, Ornaments.SidebarTile);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float centerX = Width / 2f;

        using (var glow = new GraphicsPath())
        {
            glow.AddEllipse(centerX - 60, -4, 120, 96);
            using var glowBrush = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, Theme.Gold), SurroundColors = new[] { Color.FromArgb(0, Theme.Gold) } };
            graphics.FillPath(glowBrush, glow);
        }

        Ornaments.DrawImperialEmblem(graphics, new PointF(centerX, 46), 84);

        var titleArea = new RectangleF(0, 94, Width, 40);
        using (LinearGradientBrush titleBrush = Ornaments.CreateMetallicGoldBrush(titleArea))
        using (var format = new StringFormat { Alignment = StringAlignment.Center })
        {
            graphics.DrawString("A  S  A", Theme.LogoFont, titleBrush, titleArea, format);
        }

        Ornaments.DrawDivider(graphics, new Rectangle(4, 134, Width - 8, 14), Theme.Gold);
        TextRenderer.DrawText(graphics, "ANTISPERMATTACK", Theme.SmallCapsFont, new Rectangle(0, 150, Width, 18), Theme.Gold, TextFormatFlags.HorizontalCenter);
    }
}
