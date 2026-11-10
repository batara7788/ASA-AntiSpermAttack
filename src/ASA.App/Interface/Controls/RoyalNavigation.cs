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
        Ornaments.FillDamask(graphics, ClientRectangle, Ornaments.SidebarTile, Point.Empty);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int borderX = Width - 6;
        using var thickPen = new Pen(Theme.Gold, 2f);
        using var thinPen = new Pen(Color.FromArgb(120, Theme.Gold), 1f);
        graphics.DrawLine(thickPen, borderX, 0, borderX, Height);
        graphics.DrawLine(thinPen, borderX - 4, 0, borderX - 4, Height);

        using var brush = new SolidBrush(Theme.Gold);
        for (int y = 40; y < Height; y += 80)
        {
            graphics.FillPolygon(brush, new[]
            {
                new PointF(borderX - 2, y - 6),
                new PointF(borderX + 3, y),
                new PointF(borderX - 2, y + 6),
                new PointF(borderX - 7, y)
            });
        }

        int bottomY = Height - 70;
        Ornaments.DrawFleurDeLis(graphics, brush, new PointF((Width - 10) / 2f, bottomY), 40);
        Ornaments.DrawDivider(graphics, new Rectangle(14, bottomY + 30, Width - 40, 14), Theme.Gold);
    }
}

public sealed class NavigationItem : Control
{
    private bool selected;
    private bool hovered;

    public NavigationItem(string text)
    {
        Text = text;
        Size = new Size(206, 44);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, false);
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
        Ornaments.FillDamask(eventArgs.Graphics, ClientRectangle, Ornaments.SidebarTile, new Point(-Left, -Top));
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new Rectangle(0, 0, Width - 1, Height - 1);

        if (selected)
        {
            using var fill = new LinearGradientBrush(area, Color.FromArgb(150, 122, 96, 42), Color.FromArgb(20, 122, 96, 42), LinearGradientMode.Horizontal);
            graphics.FillRectangle(fill, area);
            using var goldPen = new Pen(Theme.Gold, 1f);
            graphics.DrawLine(goldPen, 0, 0, Width * 0.8f, 0);
            graphics.DrawLine(goldPen, 0, Height - 1, Width * 0.8f, Height - 1);
            using var barBrush = new SolidBrush(Theme.Gold);
            graphics.FillRectangle(barBrush, 0, 0, 4, Height);
        }
        else if (hovered)
        {
            using var fill = new SolidBrush(Color.FromArgb(70, Theme.CardHover));
            graphics.FillRectangle(fill, area);
        }

        Color markerColor = selected ? Theme.PaleGold : Color.FromArgb(150, Theme.Gold);
        using (var markerBrush = new SolidBrush(markerColor))
        {
            float centerY = Height / 2f;
            graphics.FillPolygon(markerBrush, new[]
            {
                new PointF(22, centerY - 5),
                new PointF(27, centerY),
                new PointF(22, centerY + 5),
                new PointF(17, centerY)
            });
        }

        Color textColor = selected ? Theme.PaleGold : hovered ? Theme.Text : Theme.MutedText;
        TextRenderer.DrawText(graphics, Text, Theme.NavigationFont, new Rectangle(38, 0, Width - 40, Height), textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

public sealed class RoyalLogo : Control
{
    public RoyalLogo()
    {
        Size = new Size(206, 128);
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Ornaments.FillDamask(eventArgs.Graphics, ClientRectangle, Ornaments.SidebarTile, new Point(-Left, -Top));
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float centerX = Width / 2f;

        Ornaments.DrawCrown(graphics, new RectangleF(centerX - 22, 2, 44, 30), Theme.Gold);

        var titleFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.Top;
        TextRenderer.DrawText(graphics, "ASA", Theme.LogoFont, new Rectangle(0, 30, Width, 46), Theme.PaleGold, titleFormat);
        Ornaments.DrawDivider(graphics, new Rectangle(10, 78, Width - 20, 12), Theme.Gold);
        TextRenderer.DrawText(graphics, "AntiSpermAttack", new Font("Georgia", 10f, FontStyle.Italic), new Rectangle(0, 92, Width, 18), Theme.Gold, titleFormat);
        TextRenderer.DrawText(graphics, "императорская защита", Theme.SmallFont, new Rectangle(0, 110, Width, 16), Theme.MutedText, titleFormat);
    }
}
