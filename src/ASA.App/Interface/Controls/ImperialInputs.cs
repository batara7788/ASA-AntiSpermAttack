using System.Drawing.Drawing2D;

namespace Asa.App.Interface;

public sealed class GlassSection : FlowLayoutPanel
{
    public GlassSection()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(26, 22, 26, 16);
        Margin = new Padding(0, 0, 0, 16);
        BackColor = Theme.Card;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    public void SetFixedWidth(int width)
    {
        MinimumSize = new Size(width, 0);
        MaximumSize = new Size(width, 0);
        foreach (Control child in Controls)
        {
            if (child is Label label)
            {
                label.MaximumSize = new Size(width - Padding.Horizontal - 70, 0);
            }
        }
    }

    protected override void OnControlAdded(ControlEventArgs eventArgs)
    {
        base.OnControlAdded(eventArgs);
        if (eventArgs.Control is Label label && MaximumSize.Width > 0)
        {
            label.MaximumSize = new Size(MaximumSize.Width - Padding.Horizontal - 70, 0);
        }
    }

    public void PaintSectionBackground(Graphics graphics)
    {
        Ornaments.FillBackgroundFor(this, graphics, ClientRectangle, Ornaments.DamaskTile);
        Ornaments.DrawGlassPanel(graphics, ClientRectangle, Theme.Card, 12);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        PaintSectionBackground(eventArgs.Graphics);
    }
}

public sealed class GoldFrame : Panel
{
    public GoldFrame(Control inner, int width)
    {
        Inner = inner;
        Width = width;
        Height = 34;
        Margin = new Padding(0, 0, 12, 10);
        Padding = new Padding(10, 7, 8, 5);
        BackColor = Theme.Background;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

        inner.BackColor = InputBackground;
        inner.ForeColor = Theme.Text;
        inner.Font = Theme.BodyFont;
        inner.Dock = DockStyle.Fill;
        if (inner is TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.None;
        }
        else if (inner is NumericUpDown numeric)
        {
            numeric.BorderStyle = BorderStyle.None;
            Padding = new Padding(8, 6, 6, 4);
        }
        else if (inner is ComboBox comboBox)
        {
            Padding = new Padding(5, 4, 5, 4);
            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.DrawMode = DrawMode.OwnerDrawFixed;
            comboBox.ItemHeight = 20;
            comboBox.DrawItem += DrawComboItem;
        }

        Controls.Add(inner);
        inner.GotFocus += (_, _) => Invalidate();
        inner.LostFocus += (_, _) => Invalidate();
    }

    public static Color InputBackground => Color.FromArgb(8, 15, 40);

    private static void DrawComboItem(object? sender, DrawItemEventArgs eventArgs)
    {
        if (sender is not ComboBox comboBox || eventArgs.Index < 0)
        {
            return;
        }

        bool selected = (eventArgs.State & DrawItemState.Selected) != 0 && (eventArgs.State & DrawItemState.ComboBoxEdit) == 0;
        using var background = new SolidBrush(selected ? Theme.CardHover : InputBackground);
        eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
        string text = comboBox.Items[eventArgs.Index]?.ToString() ?? string.Empty;
        TextRenderer.DrawText(eventArgs.Graphics, text, Theme.BodyFont, Rectangle.Inflate(eventArgs.Bounds, -4, 0), selected ? Theme.PaleGold : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    public Control Inner { get; }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        Ornaments.FillBackgroundFor(this, graphics, ClientRectangle, Ornaments.DamaskTile);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new RectangleF(1, 1, Width - 3, Height - 3);
        using GraphicsPath shape = Ornaments.CreateRoundedRectangle(area, 6);
        using (var fill = new SolidBrush(InputBackground))
        {
            graphics.FillPath(fill, shape);
        }

        using (var innerShadow = new LinearGradientBrush(area, Color.FromArgb(90, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(innerShadow, area.X + 3, area.Y + 2, area.Width - 6, 6);
        }

        bool focused = Inner.ContainsFocus;
        if (focused)
        {
            using var glow = new Pen(Color.FromArgb(80, Theme.PaleGold), 4f);
            graphics.DrawPath(glow, shape);
        }

        using LinearGradientBrush gold = Ornaments.CreateMetallicGoldBrush(area);
        using var rim = new Pen(gold, focused ? 1.8f : 1.3f);
        graphics.DrawPath(rim, shape);
    }
}

public sealed class ImperialCheckBox : CheckBox
{
    private bool hovered;

    public ImperialCheckBox()
    {
        AutoSize = false;
        Cursor = Cursors.Hand;
        Font = Theme.BodyFont;
        ForeColor = Theme.Text;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override bool ShowFocusCues => false;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            UpdateSize();
        }
    }

    protected override void OnFontChanged(EventArgs eventArgs)
    {
        base.OnFontChanged(eventArgs);
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
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnCheckedChanged(EventArgs eventArgs)
    {
        Invalidate();
        base.OnCheckedChanged(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        Ornaments.FillBackgroundFor(this, graphics, ClientRectangle, Ornaments.DamaskTile);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        const float boxSize = 18;
        var box = new RectangleF(1, (Height - boxSize) / 2f, boxSize, boxSize);
        using GraphicsPath shape = Ornaments.CreateRoundedRectangle(box, 4);

        Color top = Checked ? Color.FromArgb(52, 84, 170) : Color.FromArgb(14, 24, 60);
        Color bottom = Checked ? Color.FromArgb(14, 28, 72) : Color.FromArgb(6, 12, 34);
        using (var body = new LinearGradientBrush(box, top, bottom, LinearGradientMode.Vertical))
        {
            graphics.FillPath(body, shape);
        }

        var shine = new RectangleF(box.X + 1.5f, box.Y + 1.5f, box.Width - 3, box.Height * 0.45f);
        using (GraphicsPath shinePath = Ornaments.CreateRoundedRectangle(shine, 3))
        using (var shineBrush = new LinearGradientBrush(shine, Color.FromArgb(90, Color.White), Color.FromArgb(10, Color.White), LinearGradientMode.Vertical))
        {
            graphics.FillPath(shineBrush, shinePath);
        }

        if (hovered)
        {
            using var glow = new Pen(Color.FromArgb(90, Theme.PaleGold), 3.5f);
            graphics.DrawPath(glow, shape);
        }

        using (LinearGradientBrush gold = Ornaments.CreateMetallicGoldBrush(box))
        {
            using var rim = new Pen(gold, 1.4f);
            graphics.DrawPath(rim, shape);

            if (Checked)
            {
                using var mark = new Pen(gold, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                graphics.DrawLines(mark, new[]
                {
                    new PointF(box.Left + 4.2f, box.Top + 9.4f),
                    new PointF(box.Left + 7.6f, box.Top + 13f),
                    new PointF(box.Left + 14f, box.Top + 5f)
                });
            }
        }

        Color textColor = Enabled ? (hovered ? Color.White : ForeColor) : Theme.MutedText;
        TextRenderer.DrawText(graphics, Text, Font, new Rectangle(28, 0, Width - 28, Height), textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void UpdateSize()
    {
        if (Font == null)
        {
            return;
        }

        Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font);
        Size = new Size(textSize.Width + 34, Math.Max(26, textSize.Height + 8));
    }
}
