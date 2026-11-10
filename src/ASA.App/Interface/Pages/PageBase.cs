using Asa.App.Services;

namespace Asa.App.Interface.Pages;

public abstract class PageBase : UserControl
{
    protected PageBase(ProtectionHost host, string title, string description)
    {
        Host = host;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        Padding = new Padding(28, 20, 28, 20);
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

        Content = new DamaskFlowPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        var header = new PageHeader(title, description) { Dock = DockStyle.Top };

        Controls.Add(Content);
        Controls.Add(header);
    }

    protected ProtectionHost Host { get; }

    protected FlowLayoutPanel Content { get; }

    public virtual void OnShown()
    {
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Ornaments.FillBackgroundFor(this, eventArgs.Graphics, ClientRectangle, Ornaments.DamaskTile);
    }

    protected void RunOnInterface(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    protected Panel CreateFillPanel(int height)
    {
        var panel = new OrnateFramePanel { Height = height, Margin = new Padding(0, 0, 0, 14) };
        StretchToContentWidth(panel);
        return panel;
    }

    protected void StretchToContentWidth(Control control)
    {
        Content.Resize += (_, _) => control.Width = Math.Max(300, Content.ClientSize.Width - 24);
        control.Width = Math.Max(600, Content.ClientSize.Width - 24);
    }
}

public sealed class DamaskFlowPanel : FlowLayoutPanel
{
    public DamaskFlowPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Theme.Background;
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Ornaments.FillBackgroundFor(this, eventArgs.Graphics, ClientRectangle, Ornaments.DamaskTile);
    }

    protected override void OnScroll(ScrollEventArgs eventArgs)
    {
        base.OnScroll(eventArgs);
        Invalidate(true);
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        Invalidate(true);
    }
}

public sealed class OrnateFramePanel : Panel
{
    public OrnateFramePanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Padding = new Padding(10, 12, 10, 10);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Ornaments.FillBackgroundFor(this, eventArgs.Graphics, ClientRectangle, Ornaments.DamaskTile);
        Ornaments.DrawGlassPanel(eventArgs.Graphics, ClientRectangle, Theme.Card, 10, false);
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        Invalidate();
    }
}

public sealed class PageHeader : Control
{
    private readonly string title;
    private readonly string description;

    public PageHeader(string title, string description)
    {
        this.title = title;
        this.description = description;
        Height = 96;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        Ornaments.FillBackgroundFor(this, eventArgs.Graphics, ClientRectangle, Ornaments.DamaskTile);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        Ornaments.DrawImperialEmblem(graphics, new PointF(26, 30), 54);
        Ornaments.DrawSpacedText(graphics, title.ToUpperInvariant(), Theme.TitleFont, new PointF(64, 6), 4f);
        TextRenderer.DrawText(graphics, description, Theme.BodyFont, new Point(66, 44), Theme.MutedText);

        int dividerWidth = Math.Min(Width - 30, 900);
        Ornaments.DrawDivider(graphics, new Rectangle(0, 70, dividerWidth, 16), Theme.Gold);
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        Invalidate();
    }
}
