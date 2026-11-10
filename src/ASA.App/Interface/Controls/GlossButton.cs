using System.Drawing.Drawing2D;

namespace Asa.App.Interface;

public sealed class GlossButton : Button
{
    private readonly bool primary;
    private bool hovered;
    private bool pressed;

    public GlossButton(string text, bool primary)
    {
        this.primary = primary;
        Text = text;
        Font = primary ? Theme.ButtonFontBold : Theme.ButtonFont;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        UpdateSize();
    }

    protected override bool ShowFocusCues => false;

    protected override void OnTextChanged(EventArgs eventArgs)
    {
        base.OnTextChanged(eventArgs);
        UpdateSize();
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
        pressed = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        pressed = true;
        Invalidate();
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        PaintParentBackground(eventArgs);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var area = new RectangleF(1, 1, Width - 3, Height - 3);
        float radius = Math.Min(8, area.Height / 2);
        using GraphicsPath shape = Ornaments.CreateRoundedRectangle(area, radius);

        if (hovered && Enabled)
        {
            using var glowPen = new Pen(Color.FromArgb(70, Theme.PaleGold), 4f);
            graphics.DrawPath(glowPen, shape);
        }

        Color top;
        Color bottom;
        if (primary)
        {
            top = hovered ? Color.FromArgb(252, 226, 140) : Color.FromArgb(240, 206, 104);
            bottom = hovered ? Color.FromArgb(178, 132, 36) : Color.FromArgb(150, 108, 26);
        }
        else
        {
            top = hovered ? Color.FromArgb(52, 84, 170) : Color.FromArgb(36, 62, 136);
            bottom = hovered ? Color.FromArgb(14, 28, 72) : Color.FromArgb(10, 20, 56);
        }

        if (pressed)
        {
            (top, bottom) = (bottom, top);
        }

        if (!Enabled)
        {
            top = Color.FromArgb(120, top);
            bottom = Color.FromArgb(120, bottom);
        }

        using (var body = new LinearGradientBrush(area, top, bottom, LinearGradientMode.Vertical))
        {
            graphics.FillPath(body, shape);
        }

        var shineArea = new RectangleF(area.X + 1.5f, area.Y + 1.5f, area.Width - 3, area.Height * 0.48f);
        using (GraphicsPath shine = Ornaments.CreateRoundedRectangle(shineArea, Math.Max(1, radius - 1.5f)))
        using (var shineBrush = new LinearGradientBrush(shineArea, Color.FromArgb(pressed ? 30 : 120, Color.White), Color.FromArgb(pressed ? 5 : 22, Color.White), LinearGradientMode.Vertical))
        {
            graphics.FillPath(shineBrush, shine);
        }

        using (LinearGradientBrush rim = Ornaments.CreateMetallicGoldBrush(area))
        using (var rimPen = new Pen(rim, 1.3f))
        {
            graphics.DrawPath(rimPen, shape);
        }

        Color textColor = primary ? Color.FromArgb(28, 20, 4) : Theme.PaleGold;
        if (!Enabled)
        {
            textColor = Color.FromArgb(140, primary ? 60 : 160, primary ? 50 : 160, primary ? 30 : 170);
        }

        var textArea = Rectangle.Round(area);
        if (pressed)
        {
            textArea.Offset(0, 1);
        }

        if (!primary && Enabled)
        {
            var shadowArea = textArea;
            shadowArea.Offset(0, 1);
            TextRenderer.DrawText(graphics, Text, Font, shadowArea, Color.FromArgb(4, 8, 22), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        TextRenderer.DrawText(graphics, Text, Font, textArea, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void PaintParentBackground(PaintEventArgs eventArgs)
    {
        if (Parent == null)
        {
            return;
        }

        Ornaments.FillBackgroundFor(this, eventArgs.Graphics, ClientRectangle, Ornaments.DamaskTile);
    }

    private void UpdateSize()
    {
        Size textSize = TextRenderer.MeasureText(Text, Font);
        AutoSize = false;
        Size = new Size(textSize.Width + 40, Math.Max(36, textSize.Height + 18));
    }
}
